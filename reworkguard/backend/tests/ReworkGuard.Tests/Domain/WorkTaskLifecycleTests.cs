using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using static ReworkGuard.Tests.Domain.DomainFactory;

namespace ReworkGuard.Tests.Domain;

public sealed class WorkTaskLifecycleTests
{
    [Fact]
    public void A_new_task_starts_Assigned_with_original_deliverers()
    {
        var task = NewTask(OwnerId, SecondOwnerId);

        task.Status.Should().Be(WorkTaskStatus.Assigned);
        task.OriginalDelivererIds.Should().BeEquivalentTo([OwnerId, SecondOwnerId]);
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void A_task_needs_an_assignee()
    {
        var act = () => new WorkTask(new Module(new Project("P"), "M"), "T", new DateOnly(2026, 1, 1), []);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("task.no_assignee");
    }

    [Fact]
    public void The_normal_path_records_append_only_history_and_the_completion_date()
    {
        var task = NewTask();

        task.MoveTo(WorkTaskStatus.InProgress, OwnerId, "start", Start);
        task.MoveTo(WorkTaskStatus.Completed, OwnerId, "done", Completed);

        task.Status.Should().Be(WorkTaskStatus.Completed);
        task.ActualCompletionDate.Should().Be(new DateOnly(2026, 1, 20));
        task.History.Select(h => (h.FromStatus, h.ToStatus)).Should().Equal(
            (WorkTaskStatus.Assigned, WorkTaskStatus.InProgress),
            (WorkTaskStatus.InProgress, WorkTaskStatus.Completed));
        task.History.Last().ChangedByEmployeeId.Should().Be(OwnerId);
        task.History.Last().ChangedAtUtc.Should().Be(Completed);
        task.History.Last().Reason.Should().Be("done");
    }

    [Theory]
    [InlineData(WorkTaskStatus.Assigned, WorkTaskStatus.Completed)]
    [InlineData(WorkTaskStatus.Assigned, WorkTaskStatus.UnderFix)]
    [InlineData(WorkTaskStatus.Assigned, WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.InProgress, WorkTaskStatus.Assigned)]
    [InlineData(WorkTaskStatus.InProgress, WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.Completed, WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Completed, WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.Completed, WorkTaskStatus.UnderFix)]
    [InlineData(WorkTaskStatus.Reopened, WorkTaskStatus.Completed)]
    [InlineData(WorkTaskStatus.Reopened, WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.UnderFix, WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.UnderFix, WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Closed, WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Closed, WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.Closed, WorkTaskStatus.Completed)]
    public void Illegal_manual_moves_throw_and_leave_the_task_unchanged(WorkTaskStatus from, WorkTaskStatus to)
    {
        var task = TaskIn(from);
        var historyCount = task.History.Count;

        var act = () => task.MoveTo(to, OwnerId, null, Completed.AddDays(10));

        act.Should().Throw<InvalidStateTransitionException>();
        task.Status.Should().Be(from);
        task.History.Should().HaveCount(historyCount);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Assigned)]
    [InlineData(WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Completed)]
    [InlineData(WorkTaskStatus.UnderFix)]
    public void Moving_to_Closed_must_go_through_Close(WorkTaskStatus from)
    {
        var act = () => TaskIn(from).MoveTo(WorkTaskStatus.Closed, ManagerId, null, Completed.AddDays(100));

        act.Should().Throw<InvalidStateTransitionException>().WithMessage("*close action*");
    }

    [Fact]
    public void A_team_lead_cannot_close_while_the_warranty_window_is_open()
    {
        var task = TaskIn(WorkTaskStatus.Completed);

        var act = () => task.Close(LeadId, actorIsManager: false, WarrantyDays, null, Completed.AddDays(WarrantyDays));

        act.Should().Throw<InvalidStateTransitionException>().WithMessage("*only a manager can close it early*");
    }

    [Fact]
    public void A_team_lead_can_close_once_the_warranty_window_has_passed()
    {
        var task = TaskIn(WorkTaskStatus.Completed);

        task.Close(LeadId, actorIsManager: false, WarrantyDays, "warranty over", Completed.AddDays(WarrantyDays + 1));

        task.Status.Should().Be(WorkTaskStatus.Closed);
        task.History.Last().ToStatus.Should().Be(WorkTaskStatus.Closed);
    }

    [Fact]
    public void A_manager_can_close_early()
    {
        var task = TaskIn(WorkTaskStatus.Completed);

        task.Close(ManagerId, actorIsManager: true, WarrantyDays, "accepted", Completed.AddDays(1));

        task.Status.Should().Be(WorkTaskStatus.Closed);
    }

    [Fact]
    public void Closing_after_a_fix_requires_every_bug_to_be_verified()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);
        task.MoveTo(WorkTaskStatus.Completed, OwnerId, "fixed", Completed.AddDays(6));
        var bug = task.Bugs.Single();
        bug.Status.Should().Be(BugStatus.Fixed, "completing the fix marks open bugs as fixed");

        var beforeVerify = () => task.Close(ManagerId, actorIsManager: true, WarrantyDays, null, Completed.AddDays(7));
        beforeVerify.Should().Throw<InvalidStateTransitionException>().WithMessage("*verified*");

        bug.Verify();
        task.Close(ManagerId, actorIsManager: true, WarrantyDays, null, Completed.AddDays(7));
        task.Status.Should().Be(WorkTaskStatus.Closed);
    }

    [Fact]
    public void Completing_a_fix_keeps_the_original_completion_date()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);

        task.MoveTo(WorkTaskStatus.Completed, OwnerId, "fixed", Completed.AddDays(30));

        task.ActualCompletionDate.Should().Be(DateOnly.FromDateTime(Completed));
    }

    [Theory]
    [InlineData(WorkTaskStatus.Assigned)]
    [InlineData(WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.UnderFix)]
    [InlineData(WorkTaskStatus.Closed)]
    public void Closing_from_anything_but_Completed_is_rejected(WorkTaskStatus from)
    {
        var act = () => TaskIn(from).Close(ManagerId, actorIsManager: true, WarrantyDays, null, Completed.AddDays(100));

        act.Should().Throw<InvalidStateTransitionException>();
    }
}
