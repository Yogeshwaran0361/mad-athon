using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Lifecycle;

/// <summary>
/// The only legal task status transitions, and which trigger may cause each one.
/// Everything not listed here is rejected.
/// </summary>
public static class TaskStateMachine
{
    private static readonly Dictionary<(WorkTaskStatus From, WorkTaskStatus To), TransitionTrigger[]> Transitions = new()
    {
        [(WorkTaskStatus.Assigned, WorkTaskStatus.InProgress)] = [TransitionTrigger.Manual],
        [(WorkTaskStatus.InProgress, WorkTaskStatus.Completed)] = [TransitionTrigger.Manual],
        [(WorkTaskStatus.Completed, WorkTaskStatus.Closed)] = [TransitionTrigger.Manual, TransitionTrigger.WarrantyExpired],
        [(WorkTaskStatus.Completed, WorkTaskStatus.Reopened)] = [TransitionTrigger.BugReported],
        [(WorkTaskStatus.Closed, WorkTaskStatus.Reopened)] = [TransitionTrigger.BugReported],
        [(WorkTaskStatus.Reopened, WorkTaskStatus.UnderFix)] = [TransitionTrigger.Manual, TransitionTrigger.CorrectiveWorkStarted],
        [(WorkTaskStatus.UnderFix, WorkTaskStatus.Completed)] = [TransitionTrigger.Manual],
    };

    public static IReadOnlyCollection<(WorkTaskStatus From, WorkTaskStatus To)> AllTransitions => Transitions.Keys;

    public static bool IsAllowed(WorkTaskStatus from, WorkTaskStatus to, TransitionTrigger trigger) =>
        Transitions.TryGetValue((from, to), out var triggers) && triggers.Contains(trigger);

    public static void EnsureAllowed(WorkTaskStatus from, WorkTaskStatus to, TransitionTrigger trigger)
    {
        if (!Transitions.TryGetValue((from, to), out var triggers))
        {
            throw new InvalidStateTransitionException(from, to, "this transition is not part of the task lifecycle.");
        }

        if (!triggers.Contains(trigger))
        {
            var reason = to == WorkTaskStatus.Reopened
                ? "a task can only be reopened by reporting a bug against it."
                : $"this transition cannot be caused by {trigger}.";
            throw new InvalidStateTransitionException(from, to, reason);
        }
    }

    /// <summary>Statuses a user may move the task to by hand from the given status.</summary>
    public static IReadOnlyList<WorkTaskStatus> ManualTargets(WorkTaskStatus from) =>
        Transitions
            .Where(t => t.Key.From == from && t.Value.Contains(TransitionTrigger.Manual))
            .Select(t => t.Key.To)
            .OrderBy(s => s)
            .ToList();
}
