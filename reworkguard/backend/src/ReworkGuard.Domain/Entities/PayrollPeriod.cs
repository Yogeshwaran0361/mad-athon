using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// One pay month. Its row is locked (SELECT ... FOR UPDATE) while a run for the month is generated or finalized, so
/// those steps are serialised across requests and instances. Once a run is finalized the month is locked: new
/// deduction lines that would fall due in it are routed to the next open month.
/// </summary>
public class PayrollPeriod : IVersioned
{
    private PayrollPeriod()
    {
    }

    public PayrollPeriod(YearMonth month)
    {
        Month = month.ToString();
    }

    public string Month { get; private set; } = string.Empty;

    public bool IsLocked { get; private set; }

    public int? FinalizedRunId { get; private set; }

    public DateTime? LockedAtUtc { get; private set; }

    public int Version { get; set; }

    public void LockFor(PayrollRun run, DateTime utcNow)
    {
        if (IsLocked)
        {
            throw new DomainException("payroll.month_locked", $"Payroll for {Month} is already finalized.");
        }

        IsLocked = true;
        FinalizedRunId = run.Id;
        LockedAtUtc = utcNow;
    }
}
