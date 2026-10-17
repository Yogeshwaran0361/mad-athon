using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Payroll;

/// <summary>Everything a run for one pay month is computed from, plus the employee details stored with it.</summary>
public sealed record PayrollInputs(
    PayrollRunInput Run,
    IReadOnlyDictionary<int, (string Code, string Name, PayMode PayMode, string SalaryBasis)> Employees,
    int? PreviousRunId);

/// <summary>
/// Gathers a month's run inputs: every active employee (so each gets a payslip) plus anyone who has left but still
/// owes or is owed money; Approved lines due by that month that no finalized run has taken; and the carry-forward
/// left by the most recent finalized month before it.
/// </summary>
public sealed class PayrollInputsBuilder
{
    private readonly IApplicationDbContext _db;
    private readonly PolicyProvider _policies;
    private readonly ISalaryBaseProvider _salaryBase;

    public PayrollInputsBuilder(IApplicationDbContext db, PolicyProvider policies, ISalaryBaseProvider salaryBase)
    {
        _db = db;
        _policies = policies;
        _salaryBase = salaryBase;
    }

    /// <param name="onlyEmployeeId">Restricts the inputs to one employee (for a personal preview).</param>
    public async Task<PayrollInputs> BuildAsync(YearMonth month, CancellationToken cancellationToken, int? onlyEmployeeId = null)
    {
        var monthText = month.ToString();
        var policy = await _policies.GetDeductionPolicyAsync(cancellationToken);

        var previousRunId = await _db.PayrollPeriods.AsNoTracking()
            .Where(p => p.IsLocked && string.Compare(p.Month, monthText) < 0)
            .OrderByDescending(p => p.Month)
            .Select(p => p.FinalizedRunId)
            .FirstOrDefaultAsync(cancellationToken);

        var carryQuery = _db.PayrollRunItems.AsNoTracking()
            .Join(_db.PayrollRunEmployees, i => i.PayrollRunEmployeeId, e => e.Id, (i, e) => new { Item = i, Employee = e })
            .Where(x => x.Employee.PayrollRunId == previousRunId && x.Item.CarriedForward > 0);
        if (onlyEmployeeId is { } carryFor)
        {
            carryQuery = carryQuery.Where(x => x.Employee.EmployeeId == carryFor);
        }

        var carried = previousRunId is null
            ? []
            : await carryQuery.Select(x => new { x.Employee.EmployeeId, x.Item.DeductionId, x.Item.OriginalApplyMonth, x.Item.WorkDate, x.Item.CarriedForward }).ToListAsync(cancellationToken);

        var lineQuery = _db.Deductions.AsNoTracking()
            .Where(d => d.Status == DeductionStatus.Approved && d.PayrollRunId == null && string.Compare(d.ApplyMonth, monthText) <= 0);
        if (onlyEmployeeId is { } linesFor)
        {
            lineQuery = lineQuery.Where(d => d.EmployeeId == linesFor);
        }

        var lines = await lineQuery.Select(d => new { d.Id, d.EmployeeId, d.ApplyMonth, d.WorkDate, d.Amount, d.ReversesDeductionId }).ToListAsync(cancellationToken);

        var awardQuery = _db.IncentiveAwards.AsNoTracking()
            .Where(a => a.Status == IncentiveAwardStatus.Approved && a.PayrollRunId == null && string.Compare(a.ApplyMonth, monthText) <= 0);
        if (onlyEmployeeId is { } awardsFor)
        {
            awardQuery = awardQuery.Where(a => a.EmployeeId == awardsFor);
        }

        var awards = await awardQuery.Select(a => new { a.Id, a.EmployeeId, a.Amount }).ToListAsync(cancellationToken);

        var owing = carried.Select(c => c.EmployeeId).Concat(lines.Select(l => l.EmployeeId)).Concat(awards.Select(a => a.EmployeeId)).ToHashSet();
        var employeeQuery = _db.Employees.AsNoTracking();
        employeeQuery = onlyEmployeeId is { } only
            ? employeeQuery.Where(e => e.Id == only)
            : employeeQuery.Where(e => e.IsActive || owing.Contains(e.Id));
        var employees = await employeeQuery.Select(e => new { e.Id, e.Code, e.Name, e.PayMode, e.IsActive }).ToListAsync(cancellationToken);

        var bases = await _salaryBase.GetBasesAsync(employees.Select(e => e.Id).ToList(), month, policy, cancellationToken);
        var carriedBy = carried.ToLookup(c => c.EmployeeId);
        var linesBy = lines.ToLookup(l => l.EmployeeId);
        var awardsBy = awards.Where(a => a.Amount > 0).ToLookup(a => a.EmployeeId);

        var inputs = employees
            .OrderBy(e => e.Id)
            .Select(e => new PayrollEmployeeInput(
                e.Id,
                e.IsActive,
                bases[e.Id].Amount,
                linesBy[e.Id].Select(l => new PayrollLineInput(l.Id, YearMonth.Parse(l.ApplyMonth), l.WorkDate, l.Amount, l.ReversesDeductionId)).ToList(),
                carriedBy[e.Id].Select(c => new PayrollCarryInput(c.DeductionId, YearMonth.Parse(c.OriginalApplyMonth), c.WorkDate, c.CarriedForward)).ToList())
            {
                Earnings = awardsBy[e.Id].Select(a => new PayrollEarningInput(a.Id, a.Amount)).ToList(),
            })
            .ToList();

        return new PayrollInputs(
            new PayrollRunInput(month, policy.DeductionCapPercent, inputs),
            employees.ToDictionary(e => e.Id, e => (e.Code, e.Name, e.PayMode, bases[e.Id].Basis)),
            previousRunId);
    }
}
