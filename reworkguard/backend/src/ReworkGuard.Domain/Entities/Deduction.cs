using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Deductions;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// One payroll deduction line: one employee's share of one approved corrective time log on a deductible bug.
/// Hours, rate, share and amount are a snapshot taken when the line is created and are never changed afterwards;
/// a different amount means cancelling this line (before it is finalized) or compensating it (after).
/// </summary>
public class Deduction : IVersioned
{
    private Deduction()
    {
    }

    private Deduction(
        DeductionKind kind,
        int employeeId,
        int bugId,
        int timeLogId,
        DateOnly workDate,
        YearMonth applyMonth,
        decimal hours,
        decimal hourlyRateUsed,
        decimal sharePercent,
        decimal amount,
        DateTime createdAtUtc)
    {
        Kind = kind;
        EmployeeId = employeeId;
        BugId = bugId;
        TimeLogId = timeLogId;
        WorkDate = workDate;
        SourceMonth = YearMonth.From(workDate).ToString();
        ApplyMonth = applyMonth.ToString();
        Hours = hours;
        HourlyRateUsed = hourlyRateUsed;
        SharePercent = sharePercent;
        Amount = amount;
        CreatedAtUtc = createdAtUtc;
        Status = DeductionStatus.Draft;
    }

    public int Id { get; private set; }

    public DeductionKind Kind { get; private set; }

    public int EmployeeId { get; private set; }

    public int BugId { get; private set; }

    public int TimeLogId { get; private set; }

    /// <summary>The IST date the corrective work was done.</summary>
    public DateOnly WorkDate { get; private set; }

    public string SourceMonth { get; private set; } = string.Empty;

    /// <summary>The earliest payroll month this line may be applied in.</summary>
    public string ApplyMonth { get; private set; } = string.Empty;

    /// <summary>True when the source month's payroll was already finalized, so the line was routed to a later month.</summary>
    public bool RoutedFromLockedMonth { get; private set; }

    public decimal Hours { get; private set; }

    public decimal HourlyRateUsed { get; private set; }

    public decimal SharePercent { get; private set; }

    public decimal Amount { get; private set; }

    public DeductionStatus Status { get; private set; }

    public int? ReversesDeductionId { get; private set; }

    /// <summary>Set when a manager decided this line's amount in a dispute; recalculation never overrides it.</summary>
    public bool AmountSetByDispute { get; private set; }

    /// <summary>
    /// Set when a person cancelled the line (an upheld dispute or a manager's cancellation). Recalculation never
    /// drafts that employee's share of that time log again.
    /// </summary>
    public bool CancelledByDecision { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? NotifiedAtUtc { get; private set; }

    public DateTime? DisputeWindowEndsAtUtc { get; private set; }

    /// <summary>When the employee was reminded that the dispute window is about to close (once per line).</summary>
    public DateTime? WindowReminderSentAtUtc { get; private set; }

    /// <summary>When the expiry job recorded that the window closed without a dispute (once per line).</summary>
    public DateTime? WindowExpiryRecordedAtUtc { get; private set; }

    public DateTime? AcknowledgedAtUtc { get; private set; }

    public int? ApprovedByEmployeeId { get; private set; }

    public DateTime? ApprovedAtUtc { get; private set; }

    public int? PayrollRunId { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Why a compensating line was raised.</summary>
    public string? Note { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public DeductionDispute? Dispute { get; private set; }

    public int Version { get; set; }

    public bool IsFinalized => DeductionStateMachine.IsFinalized(Status);

    public bool IsDisputeWindowOpen(DateTime utcNow) => DisputeWindowEndsAtUtc is { } end && utcNow <= end;

    /// <summary>A Standard line for one deliverer's share of an approved corrective log, at the rate valid on the work date.</summary>
    public static Deduction Create(
        int employeeId,
        int bugId,
        int timeLogId,
        DateOnly workDate,
        YearMonth applyMonth,
        decimal hours,
        decimal hourlyRate,
        decimal sharePercent,
        DateTime utcNow)
    {
        if (hours <= 0 || !Precision.HasAtMostTwoDecimals(hours))
        {
            throw new DomainException("deduction.invalid_hours", "Deduction hours must be positive with at most two decimals.");
        }

        if (sharePercent is <= 0 or > 100)
        {
            throw new DomainException("deduction.invalid_share", "A deduction share must be greater than 0 and at most 100.");
        }

        if (hourlyRate <= 0)
        {
            throw new DomainException("deduction.invalid_rate", "The hourly rate must be positive.");
        }

        var sourceMonth = YearMonth.From(workDate);
        if (applyMonth <= sourceMonth)
        {
            throw new DomainException("deduction.invalid_apply_month", "A deduction applies in a month after the work was done.");
        }

        var amount = DeductionAmount.For(hours, hourlyRate, sharePercent);
        return new Deduction(DeductionKind.Standard, employeeId, bugId, timeLogId, workDate, applyMonth, hours, hourlyRate, sharePercent, amount, utcNow)
        {
            RoutedFromLockedMonth = applyMonth > sourceMonth.Next(),
        };
    }

    /// <summary>
    /// A negative line that cancels a finalized line's full amount in a later run. It is in the employee's favour,
    /// so it skips the dispute window and starts Approved.
    /// </summary>
    public static Deduction Compensate(Deduction original, YearMonth applyMonth, string reason, int? approverId, DateTime utcNow)
    {
        if (original.Kind != DeductionKind.Standard || original.Status != DeductionStatus.Applied)
        {
            throw new DomainException("deduction.not_compensable", "Only an applied standard line can be compensated.");
        }

        var note = RequireReason(reason, "deduction.reason_required");
        var line = new Deduction(
            DeductionKind.Compensating,
            original.EmployeeId,
            original.BugId,
            original.TimeLogId,
            original.WorkDate,
            applyMonth,
            original.Hours,
            original.HourlyRateUsed,
            original.SharePercent,
            -original.Amount,
            utcNow)
        {
            ReversesDeductionId = original.Id,
            NotifiedAtUtc = utcNow,
            DisputeWindowEndsAtUtc = utcNow,
            Note = Truncate(note),
        };
        line.MoveTo(DeductionStatus.Approved);
        line.ApprovedByEmployeeId = approverId;
        line.ApprovedAtUtc = utcNow;
        return line;
    }

    /// <summary>Tells the employee and opens the dispute window, which ends at <paramref name="windowEndsAtUtc"/>.</summary>
    public void Notify(DateTime utcNow, DateTime windowEndsAtUtc)
    {
        if (windowEndsAtUtc <= utcNow)
        {
            throw new DomainException("deduction.invalid_window", "The dispute window must end in the future.");
        }

        MoveTo(DeductionStatus.Notified);
        NotifiedAtUtc = utcNow;
        DisputeWindowEndsAtUtc = windowEndsAtUtc;
    }

    /// <summary>The employee contests the line. One dispute per line, only while the window is open.</summary>
    public DeductionDispute RaiseDispute(int employeeId, string reason, DateTime utcNow)
    {
        if (employeeId != EmployeeId)
        {
            throw new DomainForbiddenException("deduction.not_owner", "Only the employee being deducted can dispute this line.");
        }

        if (Dispute is not null)
        {
            throw new DomainException("deduction.already_disputed", "This line has already been disputed once.");
        }

        if (Status != DeductionStatus.Notified)
        {
            throw new DomainException("deduction.not_disputable", $"Only a notified line can be disputed; this line is {Status}.");
        }

        if (!IsDisputeWindowOpen(utcNow))
        {
            throw new DomainException("deduction.window_closed", "The dispute window for this line has closed.");
        }

        var dispute = new DeductionDispute(employeeId, RequireReason(reason, "dispute.reason_required"), utcNow);
        MoveTo(DeductionStatus.Disputed);
        Dispute = dispute;
        return dispute;
    }

    /// <summary>
    /// A manager decides the dispute. Rejected: the line stands (Resolved). Upheld without new figures: the line is
    /// cancelled. Upheld with new hours and/or share: the line is cancelled and a smaller replacement is returned,
    /// already Resolved, with the same rate and work date.
    /// </summary>
    public Deduction? ResolveDispute(
        DisputeOutcome outcome,
        string justification,
        decimal? newHours,
        decimal? newSharePercent,
        int managerId,
        DateTime utcNow)
    {
        if (Status != DeductionStatus.Disputed || Dispute is null)
        {
            throw new DomainException("deduction.not_disputed", "This line has no open dispute.");
        }

        EnsureNotSelf(managerId, "dispute.self_resolution", "You cannot resolve a dispute about your own deduction.");
        var reason = RequireReason(justification, "dispute.justification_required");

        if (outcome == DisputeOutcome.Rejected)
        {
            if (newHours is not null || newSharePercent is not null)
            {
                throw new DomainException("dispute.figures_on_reject", "New hours or share can only be given when the dispute is upheld.");
            }

            Dispute.Resolve(outcome, reason, managerId, utcNow, replacementDeductionId: null);
            AmountSetByDispute = true;
            MoveTo(DeductionStatus.Resolved);
            return null;
        }

        Deduction? replacement = null;
        if (newHours is not null || newSharePercent is not null)
        {
            var hours = newHours ?? Hours;
            var share = newSharePercent ?? SharePercent;
            if (hours > Hours || share > SharePercent)
            {
                throw new DomainException("dispute.not_a_reduction", "An upheld dispute can only reduce the hours or the share.");
            }

            replacement = Create(EmployeeId, BugId, TimeLogId, WorkDate, YearMonth.Parse(ApplyMonth), hours, HourlyRateUsed, share, utcNow);
            if (replacement.Amount >= Amount)
            {
                throw new DomainException("dispute.not_a_reduction", "An upheld dispute must reduce the amount.");
            }

            replacement.RoutedFromLockedMonth = RoutedFromLockedMonth;
            replacement.NotifiedAtUtc = NotifiedAtUtc;
            replacement.DisputeWindowEndsAtUtc = DisputeWindowEndsAtUtc;
            replacement.AmountSetByDispute = true;
            replacement.Status = DeductionStatus.Resolved;
        }

        Dispute.Resolve(outcome, reason, managerId, utcNow, replacementDeductionId: null);
        CancelByDecision(replacement is null ? $"Dispute upheld: {reason}" : $"Replaced after dispute: {reason}", utcNow);
        return replacement;
    }

    /// <summary>Manager's final approval, after which the line can enter a payroll run.</summary>
    public void Approve(int managerId, DateTime utcNow, bool enforceDisputeWindow = true)
    {
        EnsureNotSelf(managerId, "deduction.self_approval", "You cannot approve a deduction from your own pay.");
        switch (Status)
        {
            case DeductionStatus.Notified when enforceDisputeWindow && IsDisputeWindowOpen(utcNow):
                throw new DomainException("deduction.window_open", $"The dispute window is open until {DisputeWindowEndsAtUtc:yyyy-MM-dd HH:mm} UTC.");
            case DeductionStatus.Notified:
            case DeductionStatus.Resolved:
                break;
            case DeductionStatus.Draft:
                throw new DomainException("deduction.not_notified", "The employee must be notified before the line can be approved.");
            default:
                DeductionStateMachine.EnsureAllowed(Status, DeductionStatus.Approved);
                break;
        }

        MoveTo(DeductionStatus.Approved);
        ApprovedByEmployeeId = managerId;
        ApprovedAtUtc = utcNow;
    }

    /// <summary>The employee confirms they have seen the line. Required before Applied when the acknowledgement policy is on.</summary>
    /// <summary>Notes that the closing-soon reminder went out. Only for notified lines whose window is still open.</summary>
    public void MarkWindowReminderSent(DateTime utcNow)
    {
        if (Status != DeductionStatus.Notified || DisputeWindowEndsAtUtc is null || DisputeWindowEndsAtUtc < utcNow)
        {
            throw new DomainException("deduction.no_open_window", "Only a notified line with an open dispute window can be reminded about.");
        }

        WindowReminderSentAtUtc ??= utcNow;
    }

    /// <summary>Notes that the window closed without a dispute; the line is now waiting for final approval.</summary>
    public void RecordWindowExpired(DateTime utcNow)
    {
        if (Status != DeductionStatus.Notified || DisputeWindowEndsAtUtc is null || DisputeWindowEndsAtUtc >= utcNow)
        {
            throw new DomainException("deduction.window_not_expired", "The dispute window has not closed on this line.");
        }

        WindowExpiryRecordedAtUtc ??= utcNow;
    }

    public void Acknowledge(int employeeId, DateTime utcNow)
    {
        if (employeeId != EmployeeId)
        {
            throw new DomainForbiddenException("deduction.not_owner", "Only the employee being deducted can acknowledge this line.");
        }

        if (Status is not (DeductionStatus.Notified or DeductionStatus.Resolved or DeductionStatus.Approved or DeductionStatus.Locked))
        {
            throw new DomainException("deduction.not_acknowledgeable", $"A {Status} line cannot be acknowledged.");
        }

        AcknowledgedAtUtc ??= utcNow;
    }

    /// <summary>A person's decision to drop the line. Unlike a recalculation, it is never re-drafted.</summary>
    public void CancelByDecision(string reason, DateTime utcNow)
    {
        Cancel(reason, utcNow);
        CancelledByDecision = true;
    }

    /// <summary>Drops a line that no longer matches its facts; the ledger drafts a corrected one.</summary>
    public void Cancel(string reason, DateTime utcNow)
    {
        if (Kind == DeductionKind.Compensating)
        {
            throw new DomainException("deduction.compensation_immutable", "A compensating line cannot be cancelled.");
        }

        if (Status == DeductionStatus.Disputed && Dispute?.Outcome is null)
        {
            throw new DomainException("deduction.dispute_open", "Resolve the open dispute before cancelling this line.");
        }

        var trimmed = RequireReason(reason, "deduction.reason_required");
        MoveTo(DeductionStatus.Cancelled);
        CancellationReason = Truncate(trimmed);
        ClosedAtUtc = utcNow;
    }

    /// <summary>The line was included in a finalized payroll run. From here on it is immutable.</summary>
    public void Lock(int payrollRunId)
    {
        MoveTo(DeductionStatus.Locked);
        PayrollRunId = payrollRunId;
    }

    /// <summary>The run was locked and the line deducted. Blocked when acknowledgement is required and missing while the window is still open.</summary>
    public void Apply(bool requireAcknowledgement, DateTime utcNow)
    {
        if (requireAcknowledgement && Kind == DeductionKind.Standard && AcknowledgedAtUtc is null && IsDisputeWindowOpen(utcNow))
        {
            throw new DomainException("deduction.acknowledgement_required", "The employee has not acknowledged this line and the dispute window has not lapsed.");
        }

        MoveTo(DeductionStatus.Applied);
        ClosedAtUtc = utcNow;
    }

    /// <summary>The compensating line that cancels this one has been applied.</summary>
    public void MarkReversed(DateTime utcNow)
    {
        MoveTo(DeductionStatus.Reversed);
        ClosedAtUtc = utcNow;
    }

    private void MoveTo(DeductionStatus target)
    {
        if (target == DeductionStatus.Approved && Status == DeductionStatus.Draft && Kind != DeductionKind.Compensating)
        {
            throw new DomainException("deduction.not_notified", "The employee must be notified before the line can be approved.");
        }

        DeductionStateMachine.EnsureAllowed(Status, target);
        Status = target;
    }

    private void EnsureNotSelf(int actorId, string code, string message)
    {
        if (actorId == EmployeeId)
        {
            throw new DomainForbiddenException(code, message);
        }
    }

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;

    private static string RequireReason(string? reason, string code)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < DeductionDispute.MinimumReasonLength)
        {
            throw new DomainException(code, $"A reason of at least {DeductionDispute.MinimumReasonLength} characters is required.");
        }

        return trimmed;
    }
}
