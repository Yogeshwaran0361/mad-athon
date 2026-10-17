using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;

namespace ReworkGuard.Tests.Domain;

public sealed class TaskStateMachineTests
{
    private static readonly HashSet<(WorkTaskStatus, WorkTaskStatus, TransitionTrigger)> Legal =
    [
        (WorkTaskStatus.Assigned, WorkTaskStatus.InProgress, TransitionTrigger.Manual),
        (WorkTaskStatus.InProgress, WorkTaskStatus.Completed, TransitionTrigger.Manual),
        (WorkTaskStatus.Completed, WorkTaskStatus.Closed, TransitionTrigger.Manual),
        (WorkTaskStatus.Completed, WorkTaskStatus.Closed, TransitionTrigger.WarrantyExpired),
        (WorkTaskStatus.Completed, WorkTaskStatus.Reopened, TransitionTrigger.BugReported),
        (WorkTaskStatus.Closed, WorkTaskStatus.Reopened, TransitionTrigger.BugReported),
        (WorkTaskStatus.Reopened, WorkTaskStatus.UnderFix, TransitionTrigger.Manual),
        (WorkTaskStatus.Reopened, WorkTaskStatus.UnderFix, TransitionTrigger.CorrectiveWorkStarted),
        (WorkTaskStatus.UnderFix, WorkTaskStatus.Completed, TransitionTrigger.Manual),
    ];

    public static TheoryData<WorkTaskStatus, WorkTaskStatus, TransitionTrigger> AllCombinations()
    {
        var data = new TheoryData<WorkTaskStatus, WorkTaskStatus, TransitionTrigger>();
        foreach (var from in Enum.GetValues<WorkTaskStatus>())
        {
            foreach (var to in Enum.GetValues<WorkTaskStatus>())
            {
                foreach (var trigger in Enum.GetValues<TransitionTrigger>())
                {
                    data.Add(from, to, trigger);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Only_the_documented_transitions_are_allowed(WorkTaskStatus from, WorkTaskStatus to, TransitionTrigger trigger)
    {
        var expected = Legal.Contains((from, to, trigger));

        TaskStateMachine.IsAllowed(from, to, trigger).Should().Be(expected);

        var act = () => TaskStateMachine.EnsureAllowed(from, to, trigger);
        if (expected)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<InvalidStateTransitionException>().Which.Code.Should().Be("task.invalid_transition");
        }
    }

    [Theory]
    [InlineData(WorkTaskStatus.Completed)]
    [InlineData(WorkTaskStatus.Closed)]
    public void Reopening_by_hand_is_rejected_with_a_clear_reason(WorkTaskStatus from)
    {
        var act = () => TaskStateMachine.EnsureAllowed(from, WorkTaskStatus.Reopened, TransitionTrigger.Manual);

        act.Should().Throw<InvalidStateTransitionException>().WithMessage("*only be reopened by reporting a bug*");
    }

    [Theory]
    [InlineData(WorkTaskStatus.Assigned, new[] { WorkTaskStatus.InProgress })]
    [InlineData(WorkTaskStatus.InProgress, new[] { WorkTaskStatus.Completed })]
    [InlineData(WorkTaskStatus.Completed, new[] { WorkTaskStatus.Closed })]
    [InlineData(WorkTaskStatus.Reopened, new[] { WorkTaskStatus.UnderFix })]
    [InlineData(WorkTaskStatus.UnderFix, new[] { WorkTaskStatus.Completed })]
    [InlineData(WorkTaskStatus.Closed, new WorkTaskStatus[0])]
    public void Manual_targets_never_include_reopen(WorkTaskStatus from, WorkTaskStatus[] expected)
    {
        TaskStateMachine.ManualTargets(from).Should().BeEquivalentTo(expected);
    }
}
