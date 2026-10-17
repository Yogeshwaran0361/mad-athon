using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Deductions;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Tests.Domain.Deductions;

public sealed class DeductionStateMachineTests
{
    private static readonly HashSet<(DeductionStatus, DeductionStatus)> Legal =
    [
        (DeductionStatus.Draft, DeductionStatus.Notified),
        (DeductionStatus.Draft, DeductionStatus.Approved),
        (DeductionStatus.Draft, DeductionStatus.Cancelled),
        (DeductionStatus.Notified, DeductionStatus.Disputed),
        (DeductionStatus.Notified, DeductionStatus.Approved),
        (DeductionStatus.Notified, DeductionStatus.Cancelled),
        (DeductionStatus.Disputed, DeductionStatus.Resolved),
        (DeductionStatus.Disputed, DeductionStatus.Cancelled),
        (DeductionStatus.Resolved, DeductionStatus.Approved),
        (DeductionStatus.Resolved, DeductionStatus.Cancelled),
        (DeductionStatus.Approved, DeductionStatus.Locked),
        (DeductionStatus.Approved, DeductionStatus.Cancelled),
        (DeductionStatus.Locked, DeductionStatus.Applied),
        (DeductionStatus.Applied, DeductionStatus.Reversed),
    ];

    public static TheoryData<DeductionStatus, DeductionStatus> AllPairs()
    {
        var data = new TheoryData<DeductionStatus, DeductionStatus>();
        foreach (var from in Enum.GetValues<DeductionStatus>())
        {
            foreach (var to in Enum.GetValues<DeductionStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Only_the_documented_transitions_are_legal(DeductionStatus from, DeductionStatus to)
    {
        var expected = Legal.Contains((from, to));

        DeductionStateMachine.IsAllowed(from, to).Should().Be(expected);
        var act = () => DeductionStateMachine.EnsureAllowed(from, to);
        if (expected)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<DomainException>().Which.Code.Should().Be("deduction.invalid_transition");
        }
    }

    [Fact]
    public void The_transition_table_lists_exactly_the_legal_pairs()
    {
        DeductionStateMachine.AllTransitions.Should().BeEquivalentTo(Legal);
    }

    [Theory]
    [InlineData(DeductionStatus.Locked, true)]
    [InlineData(DeductionStatus.Applied, true)]
    [InlineData(DeductionStatus.Reversed, true)]
    [InlineData(DeductionStatus.Approved, false)]
    [InlineData(DeductionStatus.Cancelled, false)]
    [InlineData(DeductionStatus.Draft, false)]
    public void Locked_applied_and_reversed_lines_are_finalized(DeductionStatus status, bool finalized)
    {
        DeductionStateMachine.IsFinalized(status).Should().Be(finalized);
    }

    [Fact]
    public void Finalized_and_terminal_states_have_no_way_back()
    {
        DeductionStateMachine.AllTransitions.Where(t => DeductionStateMachine.IsFinalized(t.From))
            .Should().OnlyContain(t => DeductionStateMachine.IsFinalized(t.To));
        DeductionStateMachine.AllTransitions.Should().NotContain(t => t.From == DeductionStatus.Cancelled || t.From == DeductionStatus.Reversed);
        DeductionStateMachine.IsLive(DeductionStatus.Cancelled).Should().BeFalse();
        DeductionStateMachine.IsLive(DeductionStatus.Reversed).Should().BeFalse();
        DeductionStateMachine.IsLive(DeductionStatus.Applied).Should().BeTrue();
    }
}
