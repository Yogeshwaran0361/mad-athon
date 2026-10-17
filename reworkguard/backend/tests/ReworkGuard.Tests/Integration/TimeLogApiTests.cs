using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.TimeLogs;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class TimeLogApiTests : ApiTestBase
{
    public TimeLogApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Regular_hours_on_an_in_progress_task_are_logged_as_pending()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var task = await Data.TaskAsync(45, owner);
        task.MoveTo(WorkTaskStatus.InProgress, owner.Id, null, Clock.UtcNow);
        await Db.SaveChangesAsync();
        var client = await Api.ClientForAsync(owner);

        var response = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 6.5m, WorkType.Regular, null, "Built the form"));

        var log = await response.ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        log.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
        log.EmployeeId.Should().Be(owner.Id);
        log.Hours.Should().Be(6.5m);
    }

    [Fact]
    public async Task Regular_hours_on_a_reopened_task_are_rejected_with_409()
    {
        var (task, _, owner, _) = await ReopenedTaskAsync();
        var client = await Api.ClientForAsync(owner);

        var response = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 2m, WorkType.Regular, null, null));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.regular_not_allowed");
        problem.GetProperty("detail").GetString().Should().Contain("Reopened");
    }

    [Fact]
    public async Task Corrective_hours_start_the_fix_and_are_audited_with_the_transition()
    {
        var (task, bug, owner, _) = await ReopenedTaskAsync();
        var client = await Api.ClientForAsync(owner);

        var response = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 3m, WorkType.Corrective, bug.Id, "Fixing"));

        var log = await response.ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        log.WorkType.Should().Be(WorkType.Corrective);
        log.BugId.Should().Be(bug.Id);
        log.TaskStatus.Should().Be(WorkTaskStatus.UnderFix);
        await using var db = FreshDb();
        var audits = await db.AuditEvents.Where(a => a.ActorEmployeeId == owner.Id).OrderBy(a => a.Id).Select(a => new { a.EntityType, a.Action }).ToListAsync();
        audits.Should().Equal(new { EntityType = "TimeLog", Action = "Logged" }, new { EntityType = "Task", Action = "StatusChanged" });
    }

    [Fact]
    public async Task Corrective_hours_without_a_bug_fail_validation()
    {
        var (task, _, owner, _) = await ReopenedTaskAsync();
        var client = await Api.ClientForAsync(owner);

        var response = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 3m, WorkType.Corrective, null, null));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain("BugId");
    }

    [Fact]
    public async Task The_24_hour_limit_holds_across_tasks_and_ignores_rejected_logs()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var first = await Data.TaskAsync(45, owner);
        var second = await Data.TaskAsync(45, owner);
        first.MoveTo(WorkTaskStatus.InProgress, owner.Id, null, Clock.UtcNow);
        second.MoveTo(WorkTaskStatus.InProgress, owner.Id, null, Clock.UtcNow);
        await Db.SaveChangesAsync();
        var client = await Api.ClientForAsync(owner);

        var big = await (await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(first.Id, Clock.Today, 20m, WorkType.Regular, null, null))).ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        var over = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(second.Id, Clock.Today, 4.5m, WorkType.Regular, null, null));
        var exact = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(second.Id, Clock.Today, 4m, WorkType.Regular, null, null));

        await over.ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.daily_limit");
        await exact.ShouldHaveStatusAsync(HttpStatusCode.Created);

        var leadClient = await Api.ClientForAsync(lead);
        await (await leadClient.PostJsonAsync($"/api/v1/timelogs/{big.Id}/reject", new RejectTimeLogRequest("Not plausible"))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        var afterReject = await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(first.Id, Clock.Today, 8m, WorkType.Regular, null, null));
        await afterReject.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrent_logs_cannot_together_exceed_24_hours()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var task = await Data.TaskAsync(45, owner);
        task.MoveTo(WorkTaskStatus.InProgress, owner.Id, null, Clock.UtcNow);
        await Db.SaveChangesAsync();
        var client = await Api.ClientForAsync(owner);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 10m, WorkType.Regular, null, null))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(2);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(2);
        await using var db = FreshDb();
        (await db.TimeLogs.Where(l => l.EmployeeId == owner.Id).SumAsync(l => l.Hours)).Should().Be(20m);
    }

    [Fact]
    public async Task A_team_lead_cannot_approve_their_own_corrective_hours()
    {
        var (task, bug, _, lead) = await ReopenedTaskAsync(leadIsAssignee: true);
        var client = await Api.ClientForAsync(lead);
        var log = await (await client.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 2m, WorkType.Corrective, bug.Id, null)))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created);

        var queue = await (await client.GetAsync(new Uri("/api/v1/timelogs/approval-queue", UriKind.Relative))).ReadAsync<PagedResult<TimeLogDto>>();
        var response = await client.PostAsync(new Uri($"/api/v1/timelogs/{log.Id}/approve", UriKind.Relative), null);

        queue.Items.Should().NotContain(l => l.Id == log.Id);
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "timelog.self_approval");
    }

    [Fact]
    public async Task Approving_corrective_hours_is_recorded_in_the_audit_trail()
    {
        var (task, bug, owner, lead) = await ReopenedTaskAsync();
        var log = await (await (await Api.ClientForAsync(owner)).PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 2.5m, WorkType.Corrective, bug.Id, null)))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created);
        var leadClient = await Api.ClientForAsync(lead);

        var queue = await (await leadClient.GetAsync(new Uri("/api/v1/timelogs/approval-queue?workType=Corrective", UriKind.Relative))).ReadAsync<PagedResult<TimeLogDto>>();
        var approved = await (await leadClient.PostAsync(new Uri($"/api/v1/timelogs/{log.Id}/approve", UriKind.Relative), null)).ReadAsync<TimeLogDto>();
        var again = await leadClient.PostAsync(new Uri($"/api/v1/timelogs/{log.Id}/approve", UriKind.Relative), null);

        queue.Items.Should().ContainSingle(l => l.Id == log.Id);
        approved.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        approved.ApprovedByEmployeeId.Should().Be(lead.Id);
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.already_decided");
        await using var db = FreshDb();
        var audit = await db.AuditEvents.SingleAsync(a => a.Action == "Approved");
        audit.ActorEmployeeId.Should().Be(lead.Id);
        audit.EntityId.Should().Be(log.Id.ToString());
        audit.DetailsJson.Should().Contain("\"workType\": \"Corrective\"");
    }

    [Fact]
    public async Task Employees_only_see_their_own_logs()
    {
        var (task, bug, owner, lead) = await ReopenedTaskAsync();
        await (await Api.ClientForAsync(owner)).PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(task.Id, Clock.Today, 1m, WorkType.Corrective, bug.Id, null));
        var other = await Data.EmployeeAsync(Role.Employee);

        var asOther = await (await (await Api.ClientForAsync(other)).GetAsync(new Uri($"/api/v1/timelogs?employeeId={owner.Id}", UriKind.Relative))).ReadAsync<PagedResult<TimeLogDto>>();
        var asLead = await (await (await Api.ClientForAsync(lead)).GetAsync(new Uri($"/api/v1/timelogs?employeeId={owner.Id}", UriKind.Relative))).ReadAsync<PagedResult<TimeLogDto>>();

        asOther.TotalCount.Should().Be(0);
        asLead.Items.Should().Contain(l => l.EmployeeId == owner.Id && l.WorkType == WorkType.Corrective);
    }

    [Fact]
    public async Task Employees_cannot_use_the_approval_endpoints()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var client = await Api.ClientForAsync(owner);

        (await client.GetAsync(new Uri("/api/v1/timelogs/approval-queue", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(new Uri("/api/v1/timelogs/1/approve", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(WorkTask Task, BugDto Bug, Employee Owner, Employee Lead)> ReopenedTaskAsync(bool leadIsAssignee = false)
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var approver = await Data.EmployeeAsync(Role.Manager);
        var qa = await Data.EmployeeAsync(Role.QA);
        var work = leadIsAssignee ? new[] { (owner, 4m), (lead, 2m) } : [(owner, 4m)];
        var task = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-5), approver, work);
        var reported = await (await (await Api.ClientForAsync(qa)).PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.High, "Fails on retry", Clock.Today)))
            .ReadAsync<BugReportedDto>(HttpStatusCode.Created);
        return (task, reported.Bug, owner, lead);
    }
}
