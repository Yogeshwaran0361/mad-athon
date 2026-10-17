using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.ChangeRequests;
using ReworkGuard.Application.TimeLogs;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class ChangeRequestApiTests : ApiTestBase
{
    private const string Base = "/api/v1/change-requests";

    public ChangeRequestApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task A_feature_request_on_an_old_closed_project_runs_end_to_end_without_reopening_or_deducting()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var old = await OldClosedTaskAsync(s);

        var cr = await CreateAsync(s.Qa, old);
        cr.Status.Should().Be(ChangeRequestStatus.Requested);
        cr.QuotedAmountExTax.Should().BeNull("QA does not see revenue");

        await (await s.Manager.PostJsonAsync($"{Base}/{cr.Id}/approve", new ApproveChangeRequestRequest(0m, [s.Priya.Id], null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "cr.invalid_hours");
        cr = await (await s.Manager.PostJsonAsync($"{Base}/{cr.Id}/approve", new ApproveChangeRequestRequest(10m, [s.Priya.Id], null))).ReadAsync<ChangeRequestDto>();
        cr.Status.Should().Be(ChangeRequestStatus.Approved);
        cr.TaskId.Should().NotBeNull().And.NotBe(old.Id);
        cr.QuotedAmountExTax.Should().Be(50000m);

        var logId = (await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(cr.TaskId!.Value, Clock.Today, 6m, WorkType.Enhancement, null, null)))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created)).Id;
        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(cr.TaskId!.Value, Clock.Today, 1m, WorkType.Regular, null, null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.change_request_task");
        (await s.PriyaClient.PostAsync(new Uri($"/api/v1/timelogs/{logId}/approve", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await s.ApproveLogAsync(logId);

        await Step(s.Lead, cr.Id, "start");
        await Step(s.Lead, cr.Id, "complete");
        (await s.Lead.PostJsonAsync($"{Base}/{cr.Id}/close", new CloseChangeRequestRequest(50000m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cr = await (await s.Finance.PostJsonAsync($"{Base}/{cr.Id}/close", new CloseChangeRequestRequest(50000m))).ReadAsync<ChangeRequestDto>();
        cr.Status.Should().Be(ChangeRequestStatus.Closed);
        cr.ReceivedAmountExTax.Should().Be(50000m);

        await using var db = FreshDb();
        var original = await db.WorkTasks.SingleAsync(t => t.Id == old.Id);
        original.Status.Should().Be(WorkTaskStatus.Closed);
        original.ReopenCount.Should().Be(0);
        (await db.WorkTasks.SingleAsync(t => t.Id == cr.TaskId)).ChangeRequestId.Should().Be(cr.Id);
        (await db.Bugs.CountAsync()).Should().Be(0);
        (await db.Deductions.CountAsync()).Should().Be(0);
        (await db.TimeLogs.SingleAsync(l => l.Id == logId)).ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        (await db.AuditEvents.Where(e => e.EntityType == "ChangeRequest" && e.EntityId == cr.Id.ToString()).OrderBy(e => e.Id).Select(e => e.Action).ToListAsync())
            .Should().Equal("Requested", "Approved", "Started", "Completed", "Closed", "ProfitShareSettled");
    }

    [Fact]
    public async Task Illegal_transitions_are_409_and_change_nothing()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var cr = await CreateAsync(s.Manager, await OldClosedTaskAsync(s));

        await (await s.Lead.PostAsync(new Uri($"{Base}/{cr.Id}/start", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "cr.invalid_transition");
        await (await s.Finance.PostJsonAsync($"{Base}/{cr.Id}/close", new CloseChangeRequestRequest(1m))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "cr.invalid_transition");
        await (await s.Manager.PostJsonAsync($"{Base}/{cr.Id}/reject", new RejectChangeRequestRequest("Not in budget"))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await s.Manager.PostJsonAsync($"{Base}/{cr.Id}/approve", new ApproveChangeRequestRequest(5m, [s.Priya.Id], null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "cr.invalid_transition");
        (await (await s.Manager.GetAsync(new Uri($"{Base}/{cr.Id}", UriKind.Relative))).ReadAsync<ChangeRequestDto>()).Status.Should().Be(ChangeRequestStatus.Rejected);
    }

    [Fact]
    public async Task Only_qa_leads_and_managers_raise_requests_and_employees_cannot_read_them()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var old = await OldClosedTaskAsync(s);
        var request = new CreateChangeRequestRequest(old.Module.ProjectId, old.ModuleId, "Bulk export", "Acme", "PO-77", 1000m);

        (await s.PriyaClient.PostJsonAsync(Base, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Finance.PostJsonAsync(Base, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PriyaClient.GetAsync(new Uri(Base, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await (await s.Lead.PostJsonAsync(Base, request with { ClientReference = "" })).ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        await (await s.Lead.PostJsonAsync(Base, request)).ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Enhancement_hours_are_rejected_on_ordinary_tasks_and_corrective_hours_on_change_request_tasks()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var regular = await Data.TaskAsync(45, s.Priya);
        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(regular.Id, Clock.Today, 2m, WorkType.Enhancement, null, null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.enhancement_not_allowed");

        var cr = await ApprovedAsync(s, await OldClosedTaskAsync(s));
        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(cr.TaskId!.Value, Clock.Today, 2m, WorkType.Corrective, 1, null)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "timelog.change_request_task");
        await (await s.PriyaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(cr.TaskId!.Value, Clock.Today, 2m, WorkType.Enhancement, 1, null)))
            .ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_bug_api_rejects_a_change_request()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var cr = await ApprovedAsync(s, await OldClosedTaskAsync(s));
        var task = await Db.WorkTasks.SingleAsync(t => t.Id == cr.TaskId);
        task.MoveTo(WorkTaskStatus.InProgress, s.Priya.Id, null, Clock.UtcNow);
        task.MoveTo(WorkTaskStatus.Completed, s.Priya.Id, null, Clock.UtcNow);
        await Db.SaveChangesAsync();

        await (await s.Qa.PostJsonAsync($"/api/v1/tasks/{cr.TaskId}/bugs", new ReportBugRequest(BugSeverity.High, "Export misses a column", Clock.Today)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "bug.change_request_task");
        (await FreshDb().Bugs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Approved_enhancement_hours_never_reach_the_deduction_ledger_but_are_paid()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var cr = await ApprovedAsync(s, await OldClosedTaskAsync(s), s.Hana);
        var logId = (await (await s.HanaClient.PostJsonAsync("/api/v1/timelogs", new LogTimeRequest(cr.TaskId!.Value, Clock.Today, 4m, WorkType.Enhancement, null, null)))
            .ReadAsync<TimeLogDto>(HttpStatusCode.Created)).Id;
        await s.ApproveLogAsync(logId);

        // A deductible bug on the scenario task still deducts only its corrective hours.
        await s.DeductibleBugAsync(2m);

        await using var db = FreshDb();
        (await db.Deductions.Select(d => d.TimeLogId).ToListAsync()).Should().NotContain(logId);
        var preview = await ReworkScenario.PreviewAsync(s.Finance, s.Hana.Id, Clock.Today.ToString("yyyy-MM"));
        preview.SalaryBase.Should().Be(4m * 250m, "Hana's 4 enhancement hours this month are paid at 250/h like any other work");
    }

    private async Task<WorkTask> OldClosedTaskAsync(ReworkScenario s)
    {
        var task = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddYears(-2), s.LeadEmployee, (s.Priya, 3m), (s.Hana, 1m));
        task.Close(s.ManagerEmployee.Id, actorIsManager: false, 45, "warranty over", Clock.UtcNow.AddYears(-1));
        await Db.SaveChangesAsync();
        return task;
    }

    private static async Task<ChangeRequestDto> CreateAsync(HttpClient who, WorkTask task) =>
        await (await who.PostJsonAsync(Base, new CreateChangeRequestRequest(task.Module.ProjectId, task.ModuleId, "Bulk export", "Acme Retail", "PO-2026-114", 50000m)))
            .ReadAsync<ChangeRequestDto>(HttpStatusCode.Created);

    private static async Task<ChangeRequestDto> ApprovedAsync(ReworkScenario s, WorkTask task, params Employee[] assignees)
    {
        var cr = await CreateAsync(s.Qa, task);
        var ids = assignees.Length == 0 ? [s.Priya.Id] : assignees.Select(a => a.Id).ToList();
        return await (await s.Manager.PostJsonAsync($"{Base}/{cr.Id}/approve", new ApproveChangeRequestRequest(10m, ids, null))).ReadAsync<ChangeRequestDto>();
    }

    private static async Task Step(HttpClient who, int id, string step) =>
        await (await who.PostAsync(new Uri($"{Base}/{id}/{step}", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.OK);
}
