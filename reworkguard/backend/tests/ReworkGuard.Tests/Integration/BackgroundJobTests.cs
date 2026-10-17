using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Jobs;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>The scheduled jobs, run directly as Hangfire would run them: own scope, no signed-in user, repeatable.</summary>
public sealed class BackgroundJobTests : ApiTestBase
{
    // Lines notified at Tue 10 Mar 2026 09:00 UTC close at Fri 13 Mar 23:59:59.999999 IST.
    private static readonly DateTime WindowEnd = new DateTime(2026, 3, 13, 18, 30, 0, DateTimeKind.Utc).AddTicks(-10);

    public BackgroundJobTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Employees_are_reminded_once_in_the_last_day_of_their_dispute_window()
    {
        var s = await ScenarioAsync();
        await NotifyAllAsync(s);

        Clock.UtcNow = WindowEnd.AddHours(-25);
        (await WindowsAsync()).RemindedLines.Should().Be(0, "more than a day is left");

        Clock.UtcNow = WindowEnd.AddHours(-20);
        (await WindowsAsync()).RemindedLines.Should().Be(2);
        (await WindowsAsync()).RemindedLines.Should().Be(0, "each line is reminded once");

        await using var db = FreshDb();
        var reminders = await db.Notifications.Where(n => n.Type == NotificationType.DisputeWindowClosing).ToListAsync();
        reminders.Select(n => n.RecipientEmployeeId).Should().BeEquivalentTo([s.Priya.Id, s.Hana.Id]);
        reminders.Should().OnlyContain(n => n.Body.Contains("closes 13 Mar 2026, 23:59 IST"));
    }

    [Fact]
    public async Task Closed_windows_are_recorded_by_the_system_and_managers_are_told_the_lines_are_ready()
    {
        var s = await ScenarioAsync();
        await NotifyAllAsync(s);

        Clock.UtcNow = WindowEnd;
        (await WindowsAsync()).ExpiredLines.Should().Be(0, "the window end is inclusive");

        Clock.UtcNow = WindowEnd.AddTicks(10);
        (await WindowsAsync()).ExpiredLines.Should().Be(2);
        (await WindowsAsync()).ExpiredLines.Should().Be(0);

        await using var db = FreshDb();
        var audit = await db.AuditEvents.Where(a => a.Action == "DisputeWindowExpired").ToListAsync();
        audit.Should().HaveCount(2).And.OnlyContain(a => a.ActorEmployeeId == null);
        var ready = await db.Notifications.SingleAsync(n => n.Type == NotificationType.DeductionsReadyForApproval);
        ready.RecipientEmployeeId.Should().Be(s.ManagerEmployee.Id);
        ready.Title.Should().Be("2 deduction lines ready for approval");
        ready.Body.Should().Contain("INR 1,150.00", "4 h × (300 × 75% for Priya + 250 × 25% for Hana)");

        // Expiry is bookkeeping only: the manager still approves.
        (await ReworkScenario.LinesAsync(s.Manager)).Should().OnlyContain(l => l.Status == DeductionStatus.Notified);
        await s.ApproveAsync((await ReworkScenario.LinesAsync(s.Manager)).Select(l => l.Id).ToArray());
    }

    [Fact]
    public async Task Disputes_open_for_more_than_two_working_days_remind_managers_daily()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await s.NotifyAsync(line.Id);
        await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute", new RaiseDisputeRequest("Half of this was a spec change"))).ShouldHaveStatusAsync(HttpStatusCode.OK);

        // Raised Tue 10 Mar; two working days run to the end of Thu 12 Mar IST.
        Clock.UtcNow = new DateTime(2026, 3, 12, 18, 0, 0, DateTimeKind.Utc);
        (await WindowsAsync()).DisputeReminders.Should().Be(0);

        Clock.UtcNow = new DateTime(2026, 3, 13, 3, 0, 0, DateTimeKind.Utc);
        (await WindowsAsync()).DisputeReminders.Should().Be(1);
        Clock.Advance(TimeSpan.FromHours(6));
        (await WindowsAsync()).DisputeReminders.Should().Be(0, "at most once a day");
        Clock.Advance(TimeSpan.FromHours(18));
        (await WindowsAsync()).DisputeReminders.Should().Be(1);

        await using var db = FreshDb();
        var reminders = await db.Notifications.Where(n => n.Type == NotificationType.DisputeAwaitingDecision).ToListAsync();
        reminders.Should().HaveCount(2).And.OnlyContain(n => n.RecipientEmployeeId == s.ManagerEmployee.Id && n.Link == "/deductions?openDispute=true");
    }

    [Fact]
    public async Task The_warranty_sweep_closes_only_tasks_past_their_warranty()
    {
        var s = await ScenarioAsync();
        var old = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-60), s.LeadEmployee, (s.Priya, 4m));

        (await SweepAsync()).Should().Be(1);
        (await SweepAsync()).Should().Be(0, "already closed");

        var detail = await (await s.Manager.GetAsync(new Uri($"/api/v1/tasks/{old.Id}", UriKind.Relative))).ReadAsync<TaskDetailDto>();
        detail.Task.Status.Should().Be(WorkTaskStatus.Closed);
        detail.History[^1].Should().BeEquivalentTo(new { ChangedByEmployeeId = (int?)null, ChangedByName = "System", Reason = "Warranty window of 45 days ended" });
        var recent = await (await s.Manager.GetAsync(new Uri($"/api/v1/tasks/{s.TaskId}", UriKind.Relative))).ReadAsync<TaskDetailDto>();
        recent.Task.Status.Should().Be(WorkTaskStatus.Completed, "delivered 9 days ago, still in warranty");

        await using var db = FreshDb();
        (await db.AuditEvents.SingleAsync(a => a.Action == "StatusChanged" && a.EntityId == old.Id.ToString())).ActorEmployeeId.Should().BeNull();
    }

    [Fact]
    public async Task A_swept_task_takes_no_more_bug_reports_they_become_change_requests()
    {
        var s = await ScenarioAsync();
        var old = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-60), s.LeadEmployee, (s.Priya, 4m));
        await SweepAsync();

        await (await s.Qa.PostJsonAsync($"/api/v1/tasks/{old.Id}/bugs", new ReportBugRequest(BugSeverity.High, "Found long after delivery", Clock.Today)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "bug.closed_outside_warranty");
    }

    [Fact]
    public async Task Daily_reminders_go_to_the_right_people_once_a_day()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync();
        await s.LogCorrectiveAsync(s.HanaClient, bugId, 1.5m);

        (await RemindersAsync()).Should().Be(new ReminderJob.Result(0, 0), "nothing is old enough yet");

        Clock.Advance(TimeSpan.FromDays(2));
        (await RemindersAsync()).Should().Be(new ReminderJob.Result(1, 1));
        (await RemindersAsync()).Should().Be(new ReminderJob.Result(0, 0), "once per day");

        await using var db = FreshDb();
        var toLead = await db.Notifications.SingleAsync(n => n.Type == NotificationType.TimeLogsAwaitingApproval);
        toLead.RecipientEmployeeId.Should().Be(s.LeadEmployee.Id);
        toLead.Title.Should().Be("1 time log waiting for approval");
        toLead.Body.Should().Contain("(1.5 h)");
        var toManager = await db.Notifications.SingleAsync(n => n.Type == NotificationType.DraftDeductionsPending);
        toManager.RecipientEmployeeId.Should().Be(s.ManagerEmployee.Id);
        toManager.Title.Should().Be("2 deduction lines not yet sent to employees");

        Clock.Advance(TimeSpan.FromDays(1));
        (await RemindersAsync()).Should().Be(new ReminderJob.Result(1, 1), "a new day");
    }

    [Fact]
    public async Task Pending_logs_from_people_without_a_team_lead_go_to_the_managers()
    {
        var s = await ScenarioAsync();
        var bugId = await s.ReportBugAsync();
        await s.LogCorrectiveAsync(s.PriyaClient, bugId, 2m);
        await using (var db = FreshDb())
        {
            var priya = await db.Employees.SingleAsync(e => e.Id == s.Priya.Id);
            priya.ChangeRole(Role.Employee, managerId: null);
            await db.SaveChangesAsync();
        }

        Clock.Advance(TimeSpan.FromDays(2));
        (await RemindersAsync()).ApproversReminded.Should().Be(1);

        await using var check = FreshDb();
        (await check.Notifications.SingleAsync(n => n.Type == NotificationType.TimeLogsAwaitingApproval)).RecipientEmployeeId.Should().Be(s.ManagerEmployee.Id);
    }

    private async Task<ReworkScenario> ScenarioAsync() => await ReworkScenario.CreateAsync(Data, Api, Clock);

    private static async Task NotifyAllAsync(ReworkScenario s)
    {
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        await s.NotifyAsync((await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}")).Select(l => l.Id).ToArray());
    }

    private Task<DeductionWindowJob.Result> WindowsAsync() =>
        Api.RunScopedAsync<DeductionWindowJob, DeductionWindowJob.Result>(j => j.RunAsync(CancellationToken.None));

    private Task<int> SweepAsync() => Api.RunScopedAsync<WarrantySweepJob, int>(j => j.RunAsync(CancellationToken.None));

    private Task<ReminderJob.Result> RemindersAsync() =>
        Api.RunScopedAsync<ReminderJob, ReminderJob.Result>(j => j.RunAsync(CancellationToken.None));
}
