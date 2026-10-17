using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Payroll;

public static class HourlyRateCalculator
{
    /// <summary>
    /// Converts an employee's pay to an hourly rate, rounded to two decimals (half away from zero) so the
    /// breakdown an employee sees can be recomputed by hand from the rate shown.
    /// </summary>
    public static decimal HourlyRate(PayProfile pay, DeductionPolicy policy)
    {
        var rate = pay.PayMode switch
        {
            PayMode.Hourly => Require(pay.HourlyRate, PayMode.Hourly),
            PayMode.Daily => Require(pay.DailyRate, PayMode.Daily) / policy.DailyHours,
            PayMode.Monthly => Require(pay.MonthlyGross, PayMode.Monthly) / policy.StandardHoursPerMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(pay)),
        };

        return Precision.Round2(rate);
    }

    private static decimal Require(decimal? value, PayMode mode) =>
        value is > 0 ? value.Value : throw new DomainException("payroll.pay_missing", $"A {mode} employee has no positive {mode} rate on file.");
}
