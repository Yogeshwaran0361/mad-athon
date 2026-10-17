using System.Reflection;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using static ReworkGuard.Tests.Domain.DomainFactory;

namespace ReworkGuard.Tests.Domain;

public sealed class TimeLogRulesTests
{
    private static readonly DateTime Now = new(2026, 2, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    [Theory]
    [InlineData(WorkTaskStatus.Assigned)]
    [InlineData(WorkTaskStatus.InProgress)]
    public void Regular_hours_are_accepted_while_the_task_is_being_built(WorkTaskStatus status)
    {
        var log = TaskIn(status).LogRegularHours(OwnerId, Today, 7.5m, "work", 0m, Now);

        log.WorkType.Should().Be(WorkType.Regular);
        log.BugId.Should().BeNull();
        log.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
        log.Hours.Should().Be(7.5m);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Completed)]
    [InlineData(WorkTaskStatus.Closed)]
    [InlineData(WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.UnderFix)]
    public void Regular_hours_are_rejected_on_delivered_or_reopened_tasks(WorkTaskStatus status)
    {
        var act = () => TaskIn(status).LogRegularHours(OwnerId, Today, 2m, null, 0m, Now);

        act.Should().Throw<DomainException>()
            .Where(e => e.Code == "timelog.regular_not_allowed")
            .WithMessage($"*{status}*corrective time*");
    }

    [Fact]
    public void The_first_corrective_hours_move_a_reopened_task_to_UnderFix()
    {
        var task = TaskIn(WorkTaskStatus.Reopened);
        var bug = task.Bugs.Single();

        var log = task.LogCorrectiveHours(bug.Id, OwnerId, Today, 3m, "fix", 0m, Now);

        log.WorkType.Should().Be(WorkType.Corrective);
        log.BugId.Should().Be(bug.Id);
        task.Status.Should().Be(WorkTaskStatus.UnderFix);
        task.History.Last().Reason.Should().Be("First corrective hours logged");
        task.History.Last().ChangedByEmployeeId.Should().Be(OwnerId);
    }

    [Fact]
    public void Further_corrective_hours_on_an_UnderFix_task_add_no_history()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);
        var historyCount = task.History.Count;

        task.LogCorrectiveHours(task.Bugs.Single().Id, SecondOwnerId, Today, 2m, null, 0m, Now);

        task.Status.Should().Be(WorkTaskStatus.UnderFix);
        task.History.Should().HaveCount(historyCount);
    }

    [Theory]
    [InlineData(WorkTaskStatus.Assigned)]
    [InlineData(WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Completed)]
    public void Corrective_hours_need_a_reopened_task_with_an_open_bug(WorkTaskStatus status)
    {
        var task = TaskIn(status);

        var act = () => task.LogCorrectiveHours(0, OwnerId, Today, 1m, null, 0m, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.bug_not_on_task");
    }

    [Fact]
    public void Corrective_hours_are_rejected_once_the_bug_is_no_longer_open()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);
        task.MoveTo(WorkTaskStatus.Completed, OwnerId, null, Now);

        var act = () => task.LogCorrectiveHours(task.Bugs.Single().Id, OwnerId, Today, 1m, null, 0m, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.bug_not_open");
    }

    [Fact]
    public void Only_assignees_can_log_time_on_a_task()
    {
        var act = () => TaskIn(WorkTaskStatus.InProgress).LogRegularHours(999, Today, 1m, null, 0m, Now);

        act.Should().Throw<DomainForbiddenException>().Which.Code.Should().Be("timelog.not_assigned");
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(23, 1)]
    [InlineData(16.25, 7.75)]
    public void Exactly_24_hours_a_day_is_allowed(double already, double hours)
    {
        var act = () => TaskIn(WorkTaskStatus.InProgress).LogRegularHours(OwnerId, Today, (decimal)hours, null, (decimal)already, Now);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(23, 1.01)]
    [InlineData(24, 0.25)]
    [InlineData(20, 4.5)]
    public void Going_over_24_hours_in_a_day_is_rejected(double already, double hours)
    {
        var act = () => TaskIn(WorkTaskStatus.InProgress).LogRegularHours(OwnerId, Today, (decimal)hours, null, (decimal)already, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.daily_limit");
    }

    [Fact]
    public void The_daily_limit_applies_to_corrective_hours_too()
    {
        var task = TaskIn(WorkTaskStatus.Reopened);

        var act = () => task.LogCorrectiveHours(task.Bugs.Single().Id, OwnerId, Today, 2m, null, 23m, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.daily_limit");
        task.Status.Should().Be(WorkTaskStatus.Reopened, "a rejected log must not start the fix");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(24.01)]
    [InlineData(1.005)]
    public void Hours_must_be_positive_at_most_24_with_two_decimals(double hours)
    {
        var act = () => TaskIn(WorkTaskStatus.InProgress).LogRegularHours(OwnerId, Today, (decimal)hours, null, 0m, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.invalid_hours");
    }

    [Fact]
    public void Time_cannot_be_logged_for_a_future_date()
    {
        var act = () => TaskIn(WorkTaskStatus.InProgress).LogRegularHours(OwnerId, Today.AddDays(1), 1m, null, 0m, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.future_date");
    }

    [Fact]
    public void An_approver_cannot_approve_or_reject_their_own_log()
    {
        var log = TaskIn(WorkTaskStatus.InProgress).LogRegularHours(OwnerId, Today, 1m, null, 0m, Now);

        var approve = () => log.Approve(OwnerId, Now);
        var reject = () => log.Reject(OwnerId, Now);

        approve.Should().Throw<SelfApprovalException>();
        reject.Should().Throw<SelfApprovalException>();
        log.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public void A_log_is_decided_once()
    {
        var log = TaskIn(WorkTaskStatus.InProgress).LogRegularHours(OwnerId, Today, 1m, null, 0m, Now);

        log.Approve(LeadId, Now);
        var again = () => log.Reject(LeadId, Now);

        log.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        log.ApprovedByEmployeeId.Should().Be(LeadId);
        log.ApprovedAtUtc.Should().Be(Now);
        again.Should().Throw<DomainException>().Which.Code.Should().Be("timelog.already_decided");
    }

    [Fact]
    public void Work_type_cannot_be_changed_after_creation()
    {
        var property = typeof(TimeLog).GetProperty(nameof(TimeLog.WorkType))!;

        property.SetMethod!.IsPublic.Should().BeFalse();
        typeof(TimeLog).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Should().NotContain(name => name.Contains("WorkType", StringComparison.Ordinal) && name.StartsWith("Set", StringComparison.Ordinal));
    }
}
