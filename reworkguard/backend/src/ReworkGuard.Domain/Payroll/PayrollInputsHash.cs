using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReworkGuard.Domain.Payroll;

/// <summary>
/// SHA-256 of a canonical text form of everything a run is computed from. If any input changes between approval and
/// finalization (a line approved or cancelled, hours approved that change a salary base, the cap), the hash changes
/// and finalization is refused.
/// </summary>
public static class PayrollInputsHash
{
    public static string Compute(PayrollRunInput input)
    {
        var text = new StringBuilder();
        text.Append("v1|").Append(input.PeriodMonth).Append('|').Append(Money(input.CapPercent)).Append('\n');
        foreach (var e in input.Employees.OrderBy(e => e.EmployeeId))
        {
            text.Append("E|").Append(e.EmployeeId).Append('|').Append(e.IsActive ? '1' : '0').Append('|').Append(Money(e.SalaryBase)).Append('\n');
            foreach (var c in e.CarriedIn.OrderBy(c => c.DeductionId))
            {
                text.Append("C|").Append(c.DeductionId).Append('|').Append(c.OriginalApplyMonth).Append('|').Append(c.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Append('|').Append(Money(c.Outstanding)).Append('\n');
            }

            foreach (var l in e.Lines.OrderBy(l => l.DeductionId))
            {
                text.Append("L|").Append(l.DeductionId).Append('|').Append(l.ApplyMonth).Append('|').Append(l.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Append('|').Append(Money(l.Amount)).Append('|').Append(l.ReversesDeductionId?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('\n');
            }

            foreach (var a in e.Earnings.OrderBy(a => a.AwardId))
            {
                text.Append("A|").Append(a.AwardId).Append('|').Append(Money(a.Amount)).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
