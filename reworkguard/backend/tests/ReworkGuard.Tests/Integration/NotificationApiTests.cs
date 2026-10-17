using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>Notifications written with business changes, the inbox API, preferences, outbox delivery and live push.</summary>
public sealed class NotificationApiTests : ApiTestBase
{
    public NotificationApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Notifying_deductions_tells_each_employee_once_in_app_and_by_email()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync(hours: 4m);
        var ids = (await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}")).Select(l => l.Id).ToArray();

        await s.NotifyAsync(ids);

        var priyaInbox = await InboxAsync(s.PriyaClient);
        var notice = priyaInbox.Items.Single(n => n.Type == NotificationType.DeductionNotified);
        notice.Title.Should().Be("Deduction notice: INR 900.00", "4 h × 300 × 75%");
        notice.Link.Should().Be("/my-deductions");
        (await InboxAsync(s.HanaClient)).Items.Should().ContainSingle(n => n.Type == NotificationType.DeductionNotified);

        await using var db = FreshDb();
        var channels = await db.OutboxMessages.Where(m => m.NotificationId == notice.Id).Select(m => m.Channel).ToListAsync();
        channels.Should().BeEquivalentTo([NotificationChannel.InApp, NotificationChannel.Email]);
    }

    [Fact]
    public async Task Bug_reports_notify_the_original_deliverers()
    {
        var s = await ScenarioAsync();
        var bugId = await s.ReportBugAsync();

        foreach (var client in new[] { s.PriyaClient, s.HanaClient })
        {
            var bug = (await InboxAsync(client)).Items.Single();
            bug.Type.Should().Be(NotificationType.BugReported);
            bug.EntityId.Should().Be(bugId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            bug.Link.Should().Be($"/tasks/{s.TaskId}");
        }

        (await InboxAsync(s.Manager)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Disputes_reach_managers_and_the_outcome_reaches_the_employee()
    {
        var s = await ScenarioAsync();
        await s.DeductibleBugAsync();
        var line = (await ReworkScenario.LinesAsync(s.PriyaClient)).Single();
        await s.NotifyAsync(line.Id);

        await (await s.PriyaClient.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute", new RaiseDisputeRequest("Half of this was a spec change"))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        var raised = (await InboxAsync(s.Manager)).Items.Single(n => n.Type == NotificationType.DisputeRaised);
        raised.Title.Should().Be($"Dispute raised by {s.Priya.Name}");
        raised.Link.Should().Be($"/deductions/{line.Id}");

        await (await s.Manager.PostJsonAsync($"/api/v1/deductions/{line.Id}/dispute/resolve", new ResolveDisputeRequest(DisputeOutcome.Rejected, "The fix was for her own defect", null, null)))
            .ShouldHaveStatusAsync(HttpStatusCode.OK);
        var outcome = (await InboxAsync(s.PriyaClient)).Items.Single(n => n.Type == NotificationType.DisputeResolved);
        outcome.Title.Should().Be("Your dispute was rejected");
        outcome.Body.Should().EndWith("Reason: The fix was for her own defect");
    }

    [Fact]
    public async Task Rejected_time_logs_notify_the_employee_with_the_reason()
    {
        var s = await ScenarioAsync();
        var bugId = await s.ReportBugAsync();
        var logId = await s.LogCorrectiveAsync(s.PriyaClient, bugId, 3m);

        await (await s.Lead.PostJsonAsync($"/api/v1/timelogs/{logId}/reject", new { reason = "Logged against the wrong bug" })).ShouldHaveStatusAsync(HttpStatusCode.OK);

        var rejected = (await InboxAsync(s.PriyaClient)).Items.Single(n => n.Type == NotificationType.TimeLogRejected);
        rejected.Body.Should().Contain("3 h").And.EndWith("Reason: Logged against the wrong bug");
    }

    [Fact]
    public async Task A_failed_business_change_writes_no_notification()
    {
        var s = await ScenarioAsync();
        var (bugId, _) = await s.DeductibleBugAsync();
        var ids = (await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}")).Select(l => l.Id).ToList();
        await s.NotifyAsync(ids[0]);

        // The second notify fails on the already-notified line, so nobody hears about the other one either.
        await (await s.Manager.PostJsonAsync("/api/v1/deductions/notify", new DeductionIdsRequest(ids))).ShouldBeProblemAsync(HttpStatusCode.Conflict);

        await using var db = FreshDb();
        (await db.Notifications.CountAsync(n => n.Type == NotificationType.DeductionNotified)).Should().Be(1);
    }

    [Fact]
    public async Task The_inbox_is_private_and_tracks_read_state()
    {
        var s = await ScenarioAsync();
        await s.ReportBugAsync();
        await s.ReportBugAsync(taskId: await AnotherDeliveredTaskAsync(s));
        var priyaInbox = await InboxAsync(s.PriyaClient);
        priyaInbox.TotalCount.Should().Be(2);
        priyaInbox.Items[0].Id.Should().BeGreaterThan(priyaInbox.Items[1].Id, "newest first");
        (await UnreadAsync(s.PriyaClient)).Should().Be(2);

        var first = priyaInbox.Items[0];
        await (await s.HanaClient.PostAsync(new Uri($"/api/v1/notifications/{first.Id}/read", UriKind.Relative), null))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound);

        var read = await (await s.PriyaClient.PostAsync(new Uri($"/api/v1/notifications/{first.Id}/read", UriKind.Relative), null)).ReadAsync<NotificationDto>();
        read.ReadAtUtc.Should().Be(Clock.UtcNow);
        (await UnreadAsync(s.PriyaClient)).Should().Be(1);
        (await InboxAsync(s.PriyaClient, unreadOnly: true)).Items.Should().ContainSingle().Which.Id.Should().Be(priyaInbox.Items[1].Id);

        (await (await s.PriyaClient.PostAsync(new Uri("/api/v1/notifications/read-all", UriKind.Relative), null)).ReadAsync<UnreadCountDto>()).Unread.Should().Be(0);
        (await UnreadAsync(s.PriyaClient)).Should().Be(0);
        (await UnreadAsync(s.HanaClient)).Should().Be(2, "Hana's inbox is untouched");
    }

    [Fact]
    public async Task Employees_can_turn_off_optional_email_but_not_formal_notices()
    {
        var s = await ScenarioAsync();
        var defaults = await (await s.PriyaClient.GetAsync(new Uri("/api/v1/notifications/preferences", UriKind.Relative))).ReadAsync<List<NotificationPreferenceDto>>();
        defaults.Should().HaveCount(Enum.GetValues<NotificationType>().Length).And.OnlyContain(p => p.EmailEnabled && p.InAppEnabled);
        defaults.Single(p => p.Type == NotificationType.DeductionNotified).EmailMandatory.Should().BeTrue();

        await (await s.PriyaClient.PutJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest([new(NotificationType.DeductionNotified, false)])))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "notification.email_mandatory");
        await (await s.PriyaClient.PutJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest([new(NotificationType.BugReported, false), new(NotificationType.BugReported, true)])))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        var updated = await (await s.PriyaClient.PutJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest([new(NotificationType.BugReported, false)])))
            .ReadAsync<List<NotificationPreferenceDto>>();
        updated.Single(p => p.Type == NotificationType.BugReported).EmailEnabled.Should().BeFalse();

        await s.ReportBugAsync();

        await using var db = FreshDb();
        var deliveries = await db.OutboxMessages.Where(m => m.Notification.Type == NotificationType.BugReported)
            .Select(m => new { m.Notification.RecipientEmployeeId, m.Channel }).ToListAsync();
        deliveries.Where(d => d.RecipientEmployeeId == s.Priya.Id).Select(d => d.Channel).Should().Equal(NotificationChannel.InApp);
        deliveries.Where(d => d.RecipientEmployeeId == s.Hana.Id).Select(d => d.Channel).Should().BeEquivalentTo([NotificationChannel.InApp, NotificationChannel.Email]);

        // Turning it back on updates the stored row.
        (await (await s.PriyaClient.PutJsonAsync("/api/v1/notifications/preferences", new UpdateNotificationPreferencesRequest([new(NotificationType.BugReported, true)])))
            .ReadAsync<List<NotificationPreferenceDto>>()).Single(p => p.Type == NotificationType.BugReported).EmailEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task The_outbox_job_sends_email_with_an_absolute_link_and_marks_messages_sent()
    {
        var s = await ScenarioAsync();
        await s.ReportBugAsync();

        var run = await ProcessOutboxAsync();

        run.Should().Be(new OutboxRunResult(Sent: 4, Skipped: 0, Retrying: 0, DeadLettered: 0), "two deliverers × (in-app + email)");
        Api.Emails.Sent.Select(e => e.ToAddress).Should().BeEquivalentTo([s.Priya.Email, s.Hana.Email]);
        var email = Api.Emails.Sent.First();
        email.Subject.Should().StartWith("[ReworkGuard] Bug reported on");
        email.TextBody.Should().Contain($"http://localhost:5173/tasks/{s.TaskId}");

        await using var db = FreshDb();
        (await db.OutboxMessages.ToListAsync()).Should().OnlyContain(m => m.Status == OutboxStatus.Sent && m.Attempts == 1 && m.ProcessedAtUtc == Clock.UtcNow);
        (await ProcessOutboxAsync()).Sent.Should().Be(0, "nothing is sent twice");
    }

    [Fact]
    public async Task Failed_email_is_retried_with_backoff_then_dead_lettered_and_can_be_retried_by_an_admin()
    {
        var s = await ScenarioAsync();
        var admin = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));
        await s.ReportBugAsync();
        Api.Emails.FailWith = new InvalidOperationException("SMTP 421 service unavailable");

        var first = await ProcessOutboxAsync();
        first.Should().Be(new OutboxRunResult(Sent: 2, Skipped: 0, Retrying: 2, DeadLettered: 0), "in-app push still works");
        (await ProcessOutboxAsync()).Should().Be(new OutboxRunResult(0, 0, 0, 0), "not due again for a minute");

        foreach (var wait in new[] { 1, 2, 4, 8 })
        {
            Clock.Advance(TimeSpan.FromMinutes(wait));
            await ProcessOutboxAsync();
        }

        var dead = await (await admin.GetAsync(new Uri("/api/v1/admin/outbox?status=DeadLettered", UriKind.Relative))).ReadAsync<PagedResult<OutboxMessageDto>>();
        dead.Items.Should().HaveCount(2).And.OnlyContain(m => m.Channel == NotificationChannel.Email && m.Attempts == 5 && m.LastError == "InvalidOperationException: SMTP 421 service unavailable");
        await (await s.Manager.GetAsync(new Uri("/api/v1/admin/outbox", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);

        Api.Emails.FailWith = null;
        await (await admin.PostAsync(new Uri($"/api/v1/admin/outbox/{dead.Items[0].Id}/retry", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await admin.PostAsync(new Uri($"/api/v1/admin/outbox/{dead.Items[0].Id}/retry", UriKind.Relative), null))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "outbox.not_dead_lettered");
        (await ProcessOutboxAsync()).Sent.Should().Be(1);
        Api.Emails.Sent.Should().ContainSingle();

        await using var db = FreshDb();
        (await db.AuditEvents.CountAsync(a => a.Action == "OutboxRetried")).Should().Be(1);
    }

    [Fact]
    public async Task Messages_for_people_who_have_left_are_skipped()
    {
        var s = await ScenarioAsync();
        await s.ReportBugAsync();
        await using (var db = FreshDb())
        {
            var hana = await db.Employees.SingleAsync(e => e.Id == s.Hana.Id);
            hana.Deactivate();
            await db.SaveChangesAsync();
        }

        var run = await ProcessOutboxAsync();

        run.Should().Be(new OutboxRunResult(Sent: 2, Skipped: 2, Retrying: 0, DeadLettered: 0));
        Api.Emails.Sent.Should().ContainSingle().Which.ToAddress.Should().Be(s.Priya.Email);

        // And they are not notified at all once inactive.
        await s.ReportBugAsync(taskId: await AnotherDeliveredTaskAsync(s));
        await using var check = FreshDb();
        (await check.Notifications.CountAsync(n => n.RecipientEmployeeId == s.Hana.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Two_workers_never_deliver_the_same_message()
    {
        var s = await ScenarioAsync();
        await s.ReportBugAsync();
        for (var i = 0; i < 4; i++)
        {
            await s.ReportBugAsync(taskId: await AnotherDeliveredTaskAsync(s));
        }

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ProcessOutboxAsync()));

        runs.Sum(r => r.Sent).Should().Be(20);
        Api.Emails.Sent.Should().HaveCount(10);
        Api.Emails.Sent.Select(e => e.ToAddress + e.Subject).Should().HaveCount(10);
        await using var db = FreshDb();
        (await db.OutboxMessages.ToListAsync()).Should().OnlyContain(m => m.Status == OutboxStatus.Sent && m.Attempts == 1);
    }

    [Fact]
    public async Task In_app_notifications_are_pushed_live_to_the_recipient_only()
    {
        var s = await ScenarioAsync();
        var received = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hanaReceived = 0;
        await using var priya = await ConnectAsync(s.PriyaClient);
        priya.On<NotificationDto>("notificationReceived", n => received.TrySetResult(n));
        await using var manager = await ConnectAsync(s.Manager);
        manager.On<NotificationDto>("notificationReceived", _ => Interlocked.Increment(ref hanaReceived));

        await s.ReportBugAsync();
        await ProcessOutboxAsync();

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pushed.Type.Should().Be(NotificationType.BugReported);
        pushed.Link.Should().Be($"/tasks/{s.TaskId}");
        hanaReceived.Should().Be(0, "the manager was not a deliverer");
    }

    [Fact]
    public async Task The_hub_refuses_anonymous_connections()
    {
        _ = Api.CreateClient();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Api.Server.BaseAddress, "/hubs/notifications"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Api.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var act = () => connection.StartAsync();

        await act.Should().ThrowAsync<HttpRequestException>().Where(e => e.StatusCode == HttpStatusCode.Unauthorized);
    }

    private async Task<ReworkScenario> ScenarioAsync() => await ReworkScenario.CreateAsync(Data, Api, Clock);

    private async Task<int> AnotherDeliveredTaskAsync(ReworkScenario s) =>
        (await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-9), s.LeadEmployee, (s.Priya, 6m), (s.Hana, 2m))).Id;

    private Task<OutboxRunResult> ProcessOutboxAsync() =>
        Api.RunScopedAsync<OutboxProcessor, OutboxRunResult>(p => p.ProcessBatchAsync(CancellationToken.None));

    private static async Task<PagedResult<NotificationDto>> InboxAsync(HttpClient client, bool unreadOnly = false) =>
        await (await client.GetAsync(new Uri($"/api/v1/notifications?pageSize=50&unreadOnly={unreadOnly}", UriKind.Relative))).ReadAsync<PagedResult<NotificationDto>>();

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await (await client.GetAsync(new Uri("/api/v1/notifications/unread-count", UriKind.Relative))).ReadAsync<UnreadCountDto>()).Unread;

    private async Task<HubConnection> ConnectAsync(HttpClient signedIn)
    {
        var token = signedIn.DefaultRequestHeaders.Authorization!.Parameter!;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Api.Server.BaseAddress, "/hubs/notifications"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Api.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
