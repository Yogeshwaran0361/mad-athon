using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Payroll;

public static class DeductionAmount
{
    /// <summary>Hours × rate × share%, rounded half away from zero to two decimals.</summary>
    public static decimal For(decimal hours, decimal hourlyRate, decimal sharePercent) =>
        Precision.Round2(hours * hourlyRate * sharePercent / 100m);
}
