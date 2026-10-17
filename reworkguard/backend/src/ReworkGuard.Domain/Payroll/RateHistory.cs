using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Domain.Payroll;

public static class RateHistory
{
    /// <summary>The entry in force on <paramref name="date"/>: the latest one whose EffectiveFrom is on or before it.</summary>
    public static EmployeeRateHistory EffectiveOn(IEnumerable<EmployeeRateHistory> history, DateOnly date) =>
        history.Where(r => r.EffectiveFrom <= date).MaxBy(r => r.EffectiveFrom)
        ?? throw new DomainException("payroll.no_rate", $"No pay rate is on file for {date:yyyy-MM-dd}.");
}
