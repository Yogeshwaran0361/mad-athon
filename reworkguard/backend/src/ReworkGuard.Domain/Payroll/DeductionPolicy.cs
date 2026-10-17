using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Payroll;

public sealed record DeductionPolicy
{
    public DeductionPolicy(decimal standardHoursPerMonth, decimal dailyHours, decimal deductionCapPercent)
    {
        if (standardHoursPerMonth <= 0 || dailyHours <= 0)
        {
            throw new DomainException("policy.invalid_hours", "Standard monthly hours and daily hours must be positive.");
        }

        if (deductionCapPercent is < 0 or > 100)
        {
            throw new DomainException("policy.invalid_cap", "The deduction cap must be between 0 and 100 percent.");
        }

        StandardHoursPerMonth = standardHoursPerMonth;
        DailyHours = dailyHours;
        DeductionCapPercent = deductionCapPercent;
    }

    public decimal StandardHoursPerMonth { get; }

    public decimal DailyHours { get; }

    public decimal DeductionCapPercent { get; }
}
