using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;
using static ReworkGuard.Tests.Domain.DomainFactory;

namespace ReworkGuard.Tests.Domain;

public sealed class BugReportingTests
{
    // Completed 2026-01-20 with a 45-day window: the last warranty day is 2026-03-06.
    private static readonly DateOnly LastWarrantyDay = new(2026, 3, 6);

    [Fact]
    public void The_last_warranty_day_is_completion_plus_warranty_days()
    {
        WarrantyPolicy.LastWarrantyDay(new DateOnly(2026, 1, 20), 45).Should().Be(LastWarrantyDay);
    }

    [Fact]
    public void A_bug_found_on_the_exact_last_warranty_day_is_within_warranty()
    {
        var task = TaskIn(WorkTaskStatus.Completed);

        var bug = task.ReportBug(WarrantyDays, BugSeverity.High, "Totals wrong", LastWarrantyDay, QaId, At(LastWarrantyDay));

        bug.WithinWarranty.Should().BeTrue();
    }

    [Fact]
    public void A_bug_found_one_day_after_the_window_is_outside_warranty_but_still_recorded()
    {
        var task = TaskIn(WorkTaskStatus.Completed);
        var dayAfter = LastWarrantyDay.AddDays(1);

        var bug = task.ReportBug(WarrantyDays, BugSeverity.High, "Totals wrong", dayAfter, QaId, At(dayAfter));

        bug.WithinWarranty.Should().BeFalse();
        bug.Status.Should().Be(BugStatus.Open);
        task.Status.Should().Be(WorkTaskStatus.Reopened);
    }

    [Fact]
    public void An_outside_warranty_bug_can_never_be_deductible_even_if_reviewed_as_own_defect()
    {
        var task = TaskIn(WorkTaskStatus.Completed);
        var dayAfter = LastWarrantyDay.AddDays(1);
        var bug = task.ReportBug(WarrantyDays, BugSeverity.High, "Totals wrong", dayAfter, QaId, At(dayAfter));

        bug.Review(RootCause.OwnDefect, causedByOriginalDelivery: true, null, ManagerId, At(dayAfter));

        bug.IsDeductible(qaMissDeductible: true).Should().BeFalse();
    }

    [Fact]
    public void Reporting_a_bug_reopens_the_task_and_counts_the_reopen()
    {
        var task = TaskIn(WorkTaskStatus.Completed);

        var bug = task.ReportBug(WarrantyDays, BugSeverity.Critical, "Crash on submit", new DateOnly(2026, 2, 1), QaId, At(new DateOnly(2026, 2, 1)));

        task.Status.Should().Be(WorkTaskStatus.Reopened);
        task.ReopenCount.Should().Be(1);
        task.Bugs.Should().ContainSingle().Which.Should().BeSameAs(bug);
        var reopen = task.History.Last();
        reopen.FromStatus.Should().Be(WorkTaskStatus.Completed);
        reopen.ToStatus.Should().Be(WorkTaskStatus.Reopened);
        reopen.ChangedByEmployeeId.Should().Be(QaId);
        reopen.Reason.Should().Contain("Crash on submit");
    }

    [Fact]
    public void A_second_defect_after_a_fix_reopens_again()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);
        task.MoveTo(WorkTaskStatus.Completed, OwnerId, "fixed", Completed.AddDays(5));
        task.Bugs.Single().Verify();

        task.ReportBug(WarrantyDays, BugSeverity.Low, "Another one", new DateOnly(2026, 2, 10), QaId, At(new DateOnly(2026, 2, 10)));

        task.ReopenCount.Should().Be(2);
        task.Bugs.Should().HaveCount(2);
    }

    [Fact]
    public void A_closed_task_inside_its_warranty_can_be_reopened_by_a_bug()
    {
        var task = TaskIn(WorkTaskStatus.Closed);

        var bug = task.ReportBug(WarrantyDays, BugSeverity.Medium, "Rounding", LastWarrantyDay, QaId, At(LastWarrantyDay));

        bug.WithinWarranty.Should().BeTrue();
        task.Status.Should().Be(WorkTaskStatus.Reopened);
        task.History.Last().FromStatus.Should().Be(WorkTaskStatus.Closed);
    }

    [Fact]
    public void A_closed_task_outside_its_warranty_rejects_the_bug_as_a_change_request()
    {
        var task = TaskIn(WorkTaskStatus.Closed);
        var dayAfter = LastWarrantyDay.AddDays(1);

        var act = () => task.ReportBug(WarrantyDays, BugSeverity.Medium, "Rounding", dayAfter, QaId, At(dayAfter));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("bug.closed_outside_warranty");
        task.Status.Should().Be(WorkTaskStatus.Closed);
        task.Bugs.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkTaskStatus.Assigned)]
    [InlineData(WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.UnderFix)]
    public void Bugs_can_only_be_reported_on_delivered_tasks(WorkTaskStatus status)
    {
        var task = TaskIn(status);

        var act = () => task.ReportBug(WarrantyDays, BugSeverity.Low, "Nope", new DateOnly(2026, 2, 1), QaId, At(new DateOnly(2026, 2, 1)));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("bug.task_not_delivered");
    }

    [Fact]
    public void The_found_date_cannot_precede_delivery()
    {
        var task = TaskIn(WorkTaskStatus.Completed);
        var today = new DateOnly(2026, 2, 1);

        var before = () => task.ReportBug(WarrantyDays, BugSeverity.Low, "Early", new DateOnly(2026, 1, 19), QaId, At(today));

        before.Should().Throw<DomainException>().Which.Code.Should().Be("bug.found_before_delivery");
    }

    [Fact]
    public void Any_found_date_on_or_after_delivery_is_accepted_even_a_later_one()
    {
        var task = TaskIn(WorkTaskStatus.Completed);
        var today = new DateOnly(2026, 2, 1);

        var bug = task.ReportBug(WarrantyDays, BugSeverity.Low, "Later", today.AddDays(1), QaId, At(today));

        bug.FoundDate.Should().Be(today.AddDays(1));
    }

    [Fact]
    public void Bug_status_moves_Open_Fixed_Verified_Closed_only_in_order()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);
        var bug = task.Bugs.Single();

        var verifyOpen = () => bug.Verify();
        verifyOpen.Should().Throw<DomainException>().Which.Code.Should().Be("bug.invalid_status");

        task.MoveTo(WorkTaskStatus.Completed, OwnerId, null, Completed.AddDays(5));
        bug.Status.Should().Be(BugStatus.Fixed);
        var closeFixed = () => bug.Close();
        closeFixed.Should().Throw<DomainException>();

        bug.Verify();
        bug.Close();
        bug.Status.Should().Be(BugStatus.Closed);
    }

    private static DateTime At(DateOnly date) => date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
}
