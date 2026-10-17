using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Deductions;

/// <summary>
/// The only legal deduction status changes. Locked, Applied and Reversed lines are finalized: their money
/// columns never change, and the only way to correct them is a compensating line in a later run.
/// </summary>
public static class DeductionStateMachine
{
    private static readonly Dictionary<DeductionStatus, DeductionStatus[]> Transitions = new()
    {
        [DeductionStatus.Draft] = [DeductionStatus.Notified, DeductionStatus.Approved, DeductionStatus.Cancelled],
        [DeductionStatus.Notified] = [DeductionStatus.Disputed, DeductionStatus.Approved, DeductionStatus.Cancelled],
        [DeductionStatus.Disputed] = [DeductionStatus.Resolved, DeductionStatus.Cancelled],
        [DeductionStatus.Resolved] = [DeductionStatus.Approved, DeductionStatus.Cancelled],
        [DeductionStatus.Approved] = [DeductionStatus.Locked, DeductionStatus.Cancelled],
        [DeductionStatus.Locked] = [DeductionStatus.Applied],
        [DeductionStatus.Applied] = [DeductionStatus.Reversed],
        [DeductionStatus.Cancelled] = [],
        [DeductionStatus.Reversed] = [],
    };

    public static IEnumerable<(DeductionStatus From, DeductionStatus To)> AllTransitions =>
        Transitions.SelectMany(t => t.Value.Select(to => (t.Key, to)));

    public static bool IsAllowed(DeductionStatus from, DeductionStatus to) => Transitions[from].Contains(to);

    public static bool IsFinalized(DeductionStatus status) => status is DeductionStatus.Locked or DeductionStatus.Applied or DeductionStatus.Reversed;

    /// <summary>Lines that still count towards what the employee owes (not cancelled, not yet reversed).</summary>
    public static bool IsLive(DeductionStatus status) => status is not (DeductionStatus.Cancelled or DeductionStatus.Reversed);

    public static void EnsureAllowed(DeductionStatus from, DeductionStatus to)
    {
        if (!IsAllowed(from, to))
        {
            throw new DomainException("deduction.invalid_transition", $"A deduction cannot move from {from} to {to}.");
        }
    }
}
