using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;

namespace ReworkGuard.Domain.Entities;

public class Bug : IVersioned
{
    private Bug()
    {
    }

    internal Bug(BugSeverity severity, string description, DateOnly foundDate, int reportedByEmployeeId, bool withinWarranty, DateTime createdAtUtc)
    {
        Severity = severity;
        Description = description;
        FoundDate = foundDate;
        ReportedByEmployeeId = reportedByEmployeeId;
        WithinWarranty = withinWarranty;
        CreatedAtUtc = createdAtUtc;
        Status = BugStatus.Open;
    }

    public int Id { get; private set; }

    public int TaskId { get; private set; }

    public WorkTask Task { get; private set; } = null!;

    public BugSeverity Severity { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DateOnly FoundDate { get; private set; }

    public int ReportedByEmployeeId { get; private set; }

    /// <summary>Computed once at creation from the task's completion date and the project's warranty window.</summary>
    public bool WithinWarranty { get; private set; }

    public RootCause? RootCause { get; private set; }

    public bool? CausedByOriginalDelivery { get; private set; }

    public int? ReviewedByEmployeeId { get; private set; }

    public string? ReviewJustification { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public BugStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Set by the manager when this defect was introduced by the fix for an earlier bug on the same task. Its deduction
    /// is then attributed to whoever delivered that fix rather than to the original deliverers.
    /// </summary>
    public int? RegressionOfBugId { get; private set; }

    public int Version { get; set; }

    public bool IsReviewed => ReviewedAtUtc is not null;

    public bool IsDeductible(bool qaMissDeductible) => DeductibilityPolicy.IsDeductible(WithinWarranty, CausedByOriginalDelivery, RootCause, qaMissDeductible);

    public void Review(RootCause rootCause, bool causedByOriginalDelivery, string? justification, int reviewerId, DateTime utcNow)
    {
        var trimmed = string.IsNullOrWhiteSpace(justification) ? null : justification.Trim();
        if (DeductibilityPolicy.RequiresJustification(rootCause, causedByOriginalDelivery)
            && (trimmed?.Length ?? 0) < DeductibilityPolicy.MinimumJustificationLength)
        {
            throw new DomainException(
                "bug.review_justification_required",
                $"A justification of at least {DeductibilityPolicy.MinimumJustificationLength} characters is required when the defect is not the deliverer's own defect.");
        }

        RootCause = rootCause;
        CausedByOriginalDelivery = causedByOriginalDelivery;
        RegressionOfBugId = null;
        ReviewJustification = trimmed;
        ReviewedByEmployeeId = reviewerId;
        ReviewedAtUtc = utcNow;
    }

    /// <summary>Marks this bug as a regression of <paramref name="earlier"/>, a fixed bug reported before it on the same task.</summary>
    public void MarkRegressionOf(Bug earlier)
    {
        if (earlier.TaskId != TaskId || earlier.Id == Id)
        {
            throw new DomainException("bug.regression_other_task", "A regression must point to an earlier bug on the same task.");
        }

        if (earlier.Id >= Id || earlier.Status == BugStatus.Open)
        {
            throw new DomainException("bug.regression_not_fixed", "A regression must point to an earlier bug whose fix was delivered.");
        }

        RegressionOfBugId = earlier.Id;
    }

    internal void MarkFixed()
    {
        EnsureStatus(BugStatus.Open, BugStatus.Fixed);
        Status = BugStatus.Fixed;
    }

    public void Verify()
    {
        EnsureStatus(BugStatus.Fixed, BugStatus.Verified);
        Status = BugStatus.Verified;
    }

    public void Close()
    {
        EnsureStatus(BugStatus.Verified, BugStatus.Closed);
        Status = BugStatus.Closed;
    }

    private void EnsureStatus(BugStatus expected, BugStatus target)
    {
        if (Status != expected)
        {
            throw new DomainException("bug.invalid_status", $"A bug can only move to {target} from {expected}; it is {Status}.");
        }
    }
}
