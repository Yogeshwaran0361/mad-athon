using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using static ReworkGuard.Tests.Domain.DomainFactory;

namespace ReworkGuard.Tests.Domain;

/// <summary>Domain rules the background jobs rely on.</summary>
public sealed class BackgroundRuleTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = Now.AddDays(3);

    // Completed 20 Jan 2026 (IST); 45-day warranty runs to 6 March inclusive.
    private static readonly DateTime LastWarrantyDayNoonUtc = new(2026, 3, 6, 6, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstDayAfterWarrantyUtc = new(2026, 3, 6, 18, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void The_sweep_leaves_a_task_alone_until_the_warranty_has_passed()
    {
        var task = TaskIn(WorkTaskStatus.Completed);
        task.CloseAfterWarranty(WarrantyDays, LastWarrantyDayNoonUtc).Should().BeNull();
        task.Status.Should().Be(WorkTaskStatus.Completed);
    }

    [Fact]
    public void After_the_warranty_the_sweep_closes_the_task_as_the_system()
    {
        var task = TaskIn(WorkTaskStatus.Completed);

        var change = task.CloseAfterWarranty(WarrantyDays, FirstDayAfterWarrantyUtc);

        change.Should().NotBeNull();
        task.Status.Should().Be(WorkTaskStatus.Closed);
        change!.ChangedByEmployeeId.Should().BeNull();
        change.Reason.Should().Be("Warranty window of 45 days ended");
    }

    [Theory]
    [InlineData(WorkTaskStatus.InProgress)]
    [InlineData(WorkTaskStatus.Reopened)]
    [InlineData(WorkTaskStatus.UnderFix)]
    [InlineData(WorkTaskStatus.Closed)]
    public void Only_completed_tasks_are_swept(WorkTaskStatus status)
    {
        var task = TaskIn(status);
        task.CloseAfterWarranty(WarrantyDays, FirstDayAfterWarrantyUtc.AddDays(100)).Should().BeNull();
        task.Status.Should().Be(status);
    }

    [Fact]
    public void A_fixed_but_unverified_bug_keeps_the_task_open()
    {
        var task = TaskIn(WorkTaskStatus.UnderFix);
        task.MoveTo(WorkTaskStatus.Completed, OwnerId, "fixed", Completed.AddDays(5));
        task.Bugs.Single().Status.Should().Be(BugStatus.Fixed);

        task.CloseAfterWarranty(WarrantyDays, FirstDayAfterWarrantyUtc.AddDays(10)).Should().BeNull();
    }

    [Fact]
    public void A_window_reminder_is_recorded_once_and_only_while_the_window_is_open()
    {
        var line = Notified();
        line.MarkWindowReminderSent(Now.AddDays(2));
        line.MarkWindowReminderSent(Now.AddDays(2).AddHours(1));
        line.WindowReminderSentAtUtc.Should().Be(Now.AddDays(2));

        var late = Notified();
        late.Invoking(l => l.MarkWindowReminderSent(WindowEnd.AddTicks(1))).Should().Throw<DomainException>().Which.Code.Should().Be("deduction.no_open_window");
        Draft().Invoking(l => l.MarkWindowReminderSent(Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Window_expiry_is_recorded_only_after_the_window_closes()
    {
        var line = Notified();
        line.Invoking(l => l.RecordWindowExpired(WindowEnd)).Should().Throw<DomainException>().Which.Code.Should().Be("deduction.window_not_expired");

        line.RecordWindowExpired(WindowEnd.AddTicks(1));
        line.WindowExpiryRecordedAtUtc.Should().Be(WindowEnd.AddTicks(1));
        line.Status.Should().Be(DeductionStatus.Notified, "approval is still the manager's decision");
    }

    [Fact]
    public void Dispute_reminders_stop_once_the_dispute_is_resolved()
    {
        var line = Notified();
        var dispute = line.RaiseDispute(10, "I was on leave that week", Now.AddHours(1));
        dispute.MarkReminderSent(Now.AddDays(3));
        dispute.ReminderSentAtUtc.Should().Be(Now.AddDays(3));

        line.ResolveDispute(DisputeOutcome.Rejected, "Logs show the work", null, null, 40, Now.AddDays(4));
        dispute.Invoking(d => d.MarkReminderSent(Now.AddDays(5))).Should().Throw<DomainException>().Which.Code.Should().Be("dispute.already_resolved");
    }

    private static Deduction Draft() =>
        Deduction.Create(10, bugId: 1, timeLogId: 7, new DateOnly(2026, 3, 9), YearMonth.Parse("2026-04"), 4m, 300m, 75m, Now);

    private static Deduction Notified()
    {
        var line = Draft();
        line.Notify(Now, WindowEnd);
        return line;
    }
}
