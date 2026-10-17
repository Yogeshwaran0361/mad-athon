using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Jobs;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Jobs;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>The real Hangfire server on MySQL: schedule, admin API, dashboard access and an end-to-end job run.</summary>
[Collection(MySqlTestGroup.Name)]
public sealed class HangfireHostingTests : IAsyncLifetime
{
    private readonly MySqlContainerFixture _mysql;
    private ApiFactory _api = null!;
    private TestData _data = null!;
    private ReworkGuard.Infrastructure.Persistence.ApplicationDbContext _db = null!;

    public HangfireHostingTests(MySqlContainerFixture mysql)
    {
        _mysql = mysql;
    }

    public async Task InitializeAsync()
    {
        var connectionString = await _mysql.CreateMigratedDatabaseAsync();
        _db = MySqlContainerFixture.CreateContext(connectionString);
        _data = new TestData(_db);
        _api = new ApiFactory(connectionString, new TestClock()) { JobsEnabled = true };
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await MySqlContainerFixture.DropDatabaseAsync(_db);
    }

    [Fact]
    public async Task The_schedule_is_registered_in_india_time_and_visible_to_admins_only()
    {
        var admin = await _api.ClientForAsync(await _data.EmployeeAsync(Role.Admin));
        var manager = await _api.ClientForAsync(await _data.EmployeeAsync(Role.Manager));

        var jobs = await (await admin.GetAsync(new Uri("/api/v1/admin/jobs", UriKind.Relative))).ReadAsync<List<ScheduledJobDto>>();

        jobs.Select(j => (j.Id, j.Cron)).Should().BeEquivalentTo(new[]
        {
            (RecurringJobs.DeductionWindows, "*/5 * * * *"),
            (RecurringJobs.OutboxDelivery, "*/15 * * * * *"),
            (RecurringJobs.Reminders, "0 9 * * 1-5"),
            (RecurringJobs.WarrantySweep, "30 1 * * *"),
        });
        jobs.Should().OnlyContain(j => j.TimeZone == "Asia/Kolkata" && j.NextRunUtc != null);
        await (await manager.GetAsync(new Uri("/api/v1/admin/jobs", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await (await admin.PostAsync(new Uri("/api/v1/admin/jobs/no-such-job/trigger", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_dashboard_needs_an_admin_session_cookie()
    {
        var adminEmployee = await _data.EmployeeAsync(Role.Admin);
        var admin = await _api.ClientForAsync(adminEmployee);
        var finance = await _api.ClientForAsync(await _data.EmployeeAsync(Role.Finance));

        // A bearer token is not enough: the dashboard only accepts its own session cookie.
        await (await admin.GetAsync(new Uri("/hangfire", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await (await finance.PostAsync(new Uri("/api/v1/admin/jobs/dashboard-session", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);

        var session = await admin.PostAsync(new Uri("/api/v1/admin/jobs/dashboard-session", UriKind.Relative), null);
        await session.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var setCookie = session.Headers.GetValues("Set-Cookie").Single();
        setCookie.Should().StartWith(DashboardSession.CookieName + "=").And.Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/hangfire");
        var cookie = setCookie.Split(';')[0];

        var browser = _api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var page = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        page.Headers.Add("Cookie", cookie);
        var dashboard = await browser.SendAsync(page);
        await dashboard.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await dashboard.Content.ReadAsStringAsync()).Should().Contain("ReworkGuard jobs");

        // A forged or stale cookie, or an admin who has since been deactivated, gets nothing.
        var forged = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        forged.Headers.Add("Cookie", DashboardSession.CookieName + "=not-a-real-session");
        await (await browser.SendAsync(forged)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);

        await using (var db = MySqlContainerFixture.CreateContext(_db.Database.GetConnectionString()!))
        {
            (await db.Employees.SingleAsync(e => e.Id == adminEmployee.Id)).Deactivate();
            await db.SaveChangesAsync();
        }

        var afterLeaving = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        afterLeaving.Headers.Add("Cookie", cookie);
        await (await browser.SendAsync(afterLeaving)).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_triggered_job_runs_on_the_server_and_delivers_the_outbox()
    {
        var admin = await _api.ClientForAsync(await _data.EmployeeAsync(Role.Admin));
        var lead = await _data.EmployeeAsync(Role.TeamLead);
        var dev = await _data.EmployeeAsync(Role.Employee);
        var qa = await _api.ClientForAsync(await _data.EmployeeAsync(Role.QA));
        var task = await _data.DeliveredTaskAsync(45, _api.Clock.UtcNow.AddDays(-3), lead, (dev, 4m));
        await (await qa.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new Application.Bugs.ReportBugRequest(BugSeverity.Low, "Typo on the invoice footer", _api.Clock.Today)))
            .ShouldHaveStatusAsync(HttpStatusCode.Created);

        await (await admin.PostAsync(new Uri($"/api/v1/admin/jobs/{RecurringJobs.OutboxDelivery}/trigger", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.Accepted);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_api.Emails.Sent.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        _api.Emails.Sent.Should().ContainSingle().Which.ToAddress.Should().Be(dev.Email);
    }

    [Fact]
    public async Task Readiness_reports_the_database_and_the_job_server()
    {
        var client = _api.CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        HttpResponseMessage ready;
        do
        {
            ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
            if (ready.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(250);
        }
        while (DateTime.UtcNow < deadline);

        await ready.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Healthy");
        await (await client.GetAsync(new Uri("/health", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }
}
