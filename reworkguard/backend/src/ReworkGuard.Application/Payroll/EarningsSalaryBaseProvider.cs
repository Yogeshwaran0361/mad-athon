using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Payroll;

/// <summary>
/// The salary base is what the employee earns for the pay month, from the pay-rate history:
/// monthly staff — the monthly gross in force on the first day of the month (a mid-month change takes effect the
/// following month); hourly staff — approved hours worked in the month at the hourly rate on each work date; daily
/// staff — for each work date, approved hours up to the standard day, as a fraction of a day, at that date's daily rate.
/// Both regular and corrective hours count: corrective work is paid, then deducted under the cap.
/// </summary>
public sealed class EarningsSalaryBaseProvider : ISalaryBaseProvider
{
    private readonly IApplicationDbContext _db;

    public EarningsSalaryBaseProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<int, SalaryBase>> GetBasesAsync(
        IReadOnlyCollection<int> employeeIds,
        YearMonth payMonth,
        DeductionPolicy policy,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.Distinct().ToList();
        var history = (await _db.EmployeeRateHistory.AsNoTracking().Where(r => ids.Contains(r.EmployeeId)).ToListAsync(cancellationToken))
            .ToLookup(r => r.EmployeeId);
        var first = payMonth.FirstDay;
        var last = payMonth.LastDay;
        var hours = (await _db.TimeLogs.AsNoTracking()
                .Where(l => ids.Contains(l.EmployeeId) && l.ApprovalStatus == ApprovalStatus.Approved && l.WorkDate >= first && l.WorkDate <= last)
                .GroupBy(l => new { l.EmployeeId, l.WorkDate })
                .Select(g => new { g.Key.EmployeeId, g.Key.WorkDate, Hours = g.Sum(l => l.Hours) })
                .ToListAsync(cancellationToken))
            .ToLookup(h => h.EmployeeId);

        var result = new Dictionary<int, SalaryBase>(ids.Count);
        foreach (var id in ids)
        {
            var rates = history[id].ToList();
            var atStart = rates.Where(r => r.EffectiveFrom <= first).MaxBy(r => r.EffectiveFrom);
            if (atStart?.PayMode == PayMode.Monthly)
            {
                result[id] = new SalaryBase(atStart.MonthlyGross!.Value, $"Monthly gross in force on {first:yyyy-MM-dd}");
                continue;
            }

            decimal amount = 0m, totalHours = 0m, days = 0m;
            foreach (var day in hours[id])
            {
                var rate = RateHistory.EffectiveOn(rates, day.WorkDate);
                switch (rate.PayMode)
                {
                    case PayMode.Hourly:
                        amount += day.Hours * rate.HourlyRate!.Value;
                        break;
                    case PayMode.Daily:
                        var fraction = Math.Min(day.Hours, policy.DailyHours) / policy.DailyHours;
                        days += fraction;
                        amount += fraction * rate.DailyRate!.Value;
                        break;
                    default:
                        // Became monthly during the month: from then on salary starts with the next month.
                        continue;
                }

                totalHours += day.Hours;
            }

            var basis = days > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{totalHours:0.##} approved hours ({Precision.Round2(days):0.##} days) in {payMonth}")
                : string.Create(CultureInfo.InvariantCulture, $"{totalHours:0.##} approved hours in {payMonth}");
            result[id] = new SalaryBase(Precision.Round2(amount), basis);
        }

        return result;
    }
}
