using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>An employee's challenge to one deduction line, and the manager's decision on it.</summary>
public class DeductionDispute : IVersioned
{
    public const int MinimumReasonLength = 10;
    public const int MaximumReasonLength = 2000;

    private DeductionDispute()
    {
    }

    internal DeductionDispute(int raisedByEmployeeId, string reason, DateTime raisedAtUtc)
    {
        if (reason.Length > MaximumReasonLength)
        {
            throw new DomainException("dispute.reason_too_long", $"A dispute reason can be at most {MaximumReasonLength} characters.");
        }

        RaisedByEmployeeId = raisedByEmployeeId;
        Reason = reason;
        RaisedAtUtc = raisedAtUtc;
    }

    public int Id { get; private set; }

    public int DeductionId { get; private set; }

    public int RaisedByEmployeeId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTime RaisedAtUtc { get; private set; }

    public DisputeOutcome? Outcome { get; private set; }

    public string? ResolutionJustification { get; private set; }

    public int? ResolvedByEmployeeId { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public int? ReplacementDeductionId { get; private set; }

    public int Version { get; set; }

    public bool IsOpen => Outcome is null;

    /// <summary>Last time managers were reminded that this dispute is waiting for a decision.</summary>
    public DateTime? ReminderSentAtUtc { get; private set; }

    public void MarkReminderSent(DateTime utcNow)
    {
        if (!IsOpen)
        {
            throw new DomainException("dispute.already_resolved", "This dispute has already been resolved.");
        }

        ReminderSentAtUtc = utcNow;
    }

    internal void Resolve(DisputeOutcome outcome, string justification, int managerId, DateTime utcNow, int? replacementDeductionId)
    {
        if (justification.Length > MaximumReasonLength)
        {
            throw new DomainException("dispute.justification_too_long", $"A justification can be at most {MaximumReasonLength} characters.");
        }

        Outcome = outcome;
        ResolutionJustification = justification;
        ResolvedByEmployeeId = managerId;
        ResolvedAtUtc = utcNow;
        ReplacementDeductionId = replacementDeductionId;
    }

    /// <summary>Links the replacement line once it has an id.</summary>
    public void LinkReplacement(int replacementDeductionId)
    {
        if (Outcome != DisputeOutcome.Upheld || ReplacementDeductionId is not null)
        {
            throw new DomainException("dispute.replacement_invalid", "Only an upheld dispute without a replacement can be linked to one.");
        }

        ReplacementDeductionId = replacementDeductionId;
    }
}
