using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Tests.Domain;

public sealed class ChangeRequestTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static readonly (ChangeRequestStatus From, ChangeRequestStatus To)[] Legal =
    [
        (ChangeRequestStatus.Requested, ChangeRequestStatus.Approved),
        (ChangeRequestStatus.Requested, ChangeRequestStatus.Rejected),
        (ChangeRequestStatus.Approved, ChangeRequestStatus.InProgress),
        (ChangeRequestStatus.InProgress, ChangeRequestStatus.Completed),
        (ChangeRequestStatus.Completed, ChangeRequestStatus.Closed),
    ];

    public static TheoryData<ChangeRequestStatus, ChangeRequestStatus> AllPairs()
    {
        var data = new TheoryData<ChangeRequestStatus, ChangeRequestStatus>();
        foreach (var from in Enum.GetValues<ChangeRequestStatus>())
        {
            foreach (var to in Enum.GetValues<ChangeRequestStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Only_the_documented_transitions_are_allowed(ChangeRequestStatus from, ChangeRequestStatus to)
    {
        var cr = In(from);
        var act = () => Move(cr, to);

        if (Legal.Contains((from, to)))
        {
            act();
            cr.Status.Should().Be(to);
            ChangeRequest.CanMove(from, to).Should().BeTrue();
        }
        else
        {
            act.Should().Throw<DomainException>().Which.Code.Should().Be("cr.invalid_transition");
            cr.Status.Should().Be(from);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Approving_needs_positive_hours(decimal hours)
    {
        var cr = NewRequest();
        cr.Invoking(c => c.Approve(DomainFactory.ManagerId, hours)).Should().Throw<DomainException>().Which.Code.Should().Be("cr.invalid_hours");
        cr.Status.Should().Be(ChangeRequestStatus.Requested);
    }

    [Fact]
    public void A_client_reference_is_required()
    {
        var act = () => new ChangeRequest(1, 1, "Export to CSV", "Acme", " ", 1000m, Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("cr.client_reference_required");
    }

    [Theory]
    [InlineData(ChangeRequestStatus.Approved)]
    [InlineData(ChangeRequestStatus.InProgress)]
    public void Enhancement_hours_are_allowed_on_an_open_change_request_task(ChangeRequestStatus status)
    {
        var (cr, task) = CrTask(status);

        var log = task.LogEnhancementHours(cr, DomainFactory.OwnerId, Today, 3m, null, 0m, Now);

        log.WorkType.Should().Be(WorkType.Enhancement);
        log.BugId.Should().BeNull();
        task.Status.Should().Be(WorkTaskStatus.Assigned);
    }

    [Theory]
    [InlineData(ChangeRequestStatus.Completed)]
    [InlineData(ChangeRequestStatus.Closed)]
    public void Enhancement_hours_are_rejected_once_the_request_is_finished(ChangeRequestStatus status)
    {
        var (cr, task) = CrTask(status);
        task.Invoking(t => t.LogEnhancementHours(cr, DomainFactory.OwnerId, Today, 3m, null, 0m, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("timelog.change_request_not_open");
    }

    [Fact]
    public void Enhancement_hours_are_rejected_on_ordinary_tasks()
    {
        var task = DomainFactory.TaskIn(WorkTaskStatus.InProgress);
        task.Invoking(t => t.LogEnhancementHours(Approved(), DomainFactory.OwnerId, Today, 3m, null, 0m, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("timelog.enhancement_not_allowed");
    }

    [Fact]
    public void Daily_limit_still_applies_to_enhancement_hours()
    {
        var (cr, task) = CrTask(ChangeRequestStatus.Approved);
        task.Invoking(t => t.LogEnhancementHours(cr, DomainFactory.OwnerId, Today, 5m, null, 20m, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("timelog.daily_limit");
    }

    [Fact]
    public void Regular_and_corrective_hours_are_rejected_on_a_change_request_task()
    {
        var (_, task) = CrTask(ChangeRequestStatus.Approved);

        task.Invoking(t => t.LogRegularHours(DomainFactory.OwnerId, Today, 2m, null, 0m, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("timelog.change_request_task");
        task.Invoking(t => t.LogCorrectiveHours(1, DomainFactory.OwnerId, Today, 2m, null, 0m, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("timelog.change_request_task");
    }

    [Fact]
    public void A_change_request_task_can_never_take_a_bug_or_be_reopened()
    {
        var (_, task) = CrTask(ChangeRequestStatus.InProgress);
        task.MoveTo(WorkTaskStatus.InProgress, DomainFactory.OwnerId, null, Now);
        task.MoveTo(WorkTaskStatus.Completed, DomainFactory.OwnerId, null, Now);

        task.Invoking(t => t.ReportBug(45, BugSeverity.High, "Broken export", Today, DomainFactory.QaId, Now))
            .Should().Throw<DomainException>().Which.Code.Should().Be("bug.change_request_task");
        task.ReopenCount.Should().Be(0);
    }

    [Theory]
    [InlineData(ChangeRequestStatus.Approved, WorkTaskStatus.Assigned, ChangeRequestStatus.Approved, new string[0])]
    [InlineData(ChangeRequestStatus.Approved, WorkTaskStatus.InProgress, ChangeRequestStatus.InProgress, new[] { "Started" })]
    [InlineData(ChangeRequestStatus.InProgress, WorkTaskStatus.InProgress, ChangeRequestStatus.InProgress, new string[0])]
    [InlineData(ChangeRequestStatus.InProgress, WorkTaskStatus.Completed, ChangeRequestStatus.Completed, new[] { "Completed" })]
    [InlineData(ChangeRequestStatus.Approved, WorkTaskStatus.Completed, ChangeRequestStatus.Completed, new[] { "Started", "Completed" })]
    [InlineData(ChangeRequestStatus.Completed, WorkTaskStatus.Completed, ChangeRequestStatus.Completed, new string[0])]
    [InlineData(ChangeRequestStatus.Closed, WorkTaskStatus.Closed, ChangeRequestStatus.Closed, new string[0])]
    public void A_change_request_follows_its_task(ChangeRequestStatus from, WorkTaskStatus taskStatus, ChangeRequestStatus expected, string[] actions)
    {
        var cr = In(from);

        cr.FollowTask(taskStatus).Should().Equal(actions);
        cr.Status.Should().Be(expected);
    }

    [Fact]
    public void The_deduction_engine_refuses_enhancement_hours()
    {
        var act = () => DeductionWorkTypeGuard.EnsureDeductible(7, WorkType.Enhancement);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Enhancement*must never reach the deduction engine*");
        ((Action)(() => DeductionWorkTypeGuard.EnsureDeductible(7, WorkType.Regular))).Should().Throw<InvalidOperationException>();
        ((Action)(() => DeductionWorkTypeGuard.EnsureDeductible(7, WorkType.Corrective))).Should().NotThrow();
    }

    private static ChangeRequest NewRequest() => new(1, 1, "Export to CSV", "Acme", "PO-1", 1000m, Now);

    private static ChangeRequest Approved()
    {
        var cr = NewRequest();
        cr.Approve(DomainFactory.ManagerId, 10m);
        return cr;
    }

    private static (ChangeRequest Cr, WorkTask Task) CrTask(ChangeRequestStatus status)
    {
        var cr = In(status);
        var module = new Module(new Project("P"), "M");
        return (cr, WorkTask.ForChangeRequest(module, cr.Id, "CR task", Today.AddDays(14), [DomainFactory.OwnerId]));
    }

    private static ChangeRequest In(ChangeRequestStatus status)
    {
        var cr = NewRequest();
        ChangeRequestStatus[] path = status == ChangeRequestStatus.Rejected
            ? [ChangeRequestStatus.Rejected]
            : [ChangeRequestStatus.Approved, ChangeRequestStatus.InProgress, ChangeRequestStatus.Completed, ChangeRequestStatus.Closed];
        foreach (var step in path.TakeWhile(_ => cr.Status != status))
        {
            Move(cr, step);
        }

        return cr;
    }

    private static void Move(ChangeRequest cr, ChangeRequestStatus to)
    {
        switch (to)
        {
            case ChangeRequestStatus.Approved: cr.Approve(DomainFactory.ManagerId, 10m); break;
            case ChangeRequestStatus.Rejected: cr.Reject("Out of budget"); break;
            case ChangeRequestStatus.InProgress: cr.Start(); break;
            case ChangeRequestStatus.Completed: cr.Complete(); break;
            case ChangeRequestStatus.Closed: cr.Close(1000m); break;
            case ChangeRequestStatus.Requested: throw new DomainException("cr.invalid_transition", "Nothing moves back to Requested.");
        }
    }
}
