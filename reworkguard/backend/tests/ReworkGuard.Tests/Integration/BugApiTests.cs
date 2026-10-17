using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class BugApiTests : ApiTestBase
{
    public BugApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task QA_reports_a_bug_which_reopens_the_task_and_names_the_original_deliverers()
    {
        var (task, qa, a, b) = await DeliveredTaskWithTwoOwnersAsync(completedDaysAgo: 10);
        var client = await Api.ClientForAsync(qa);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.High, "Card number accepted with 15 digits", Clock.Today));

        var reported = await response.ReadAsync<BugReportedDto>(HttpStatusCode.Created);
        reported.Bug.WithinWarranty.Should().BeTrue();
        reported.Bug.Status.Should().Be(BugStatus.Open);
        reported.Bug.TaskStatus.Should().Be(WorkTaskStatus.Reopened);
        reported.ReopenCount.Should().Be(1);
        reported.WarrantyEndsOn.Should().Be(Clock.Today.AddDays(-10).AddDays(45));
        reported.OriginalDeliverers.Select(d => d.EmployeeId).Should().BeEquivalentTo([a.Id, b.Id]);

        await using var db = FreshDb();
        var audits = await db.AuditEvents.Where(e => e.EntityType != "Employee").OrderBy(e => e.Id).ToListAsync(); // sign-ins are audited on the employee
        audits.Select(e => (e.EntityType, e.Action)).Should().Equal(("Bug", "Reported"), ("Task", "StatusChanged"));
        audits.Should().OnlyContain(e => e.ActorEmployeeId == qa.Id);
        (await db.TaskStateHistory.Where(h => h.TaskId == task.Id).OrderBy(h => h.Id).LastAsync()).ToStatus.Should().Be(WorkTaskStatus.Reopened);
    }

    [Fact]
    public async Task A_bug_after_the_warranty_window_is_stored_as_outside_warranty()
    {
        var (task, qa, _, _) = await DeliveredTaskWithTwoOwnersAsync(completedDaysAgo: 46);
        var client = await Api.ClientForAsync(qa);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.Low, "Label typo on receipt", Clock.Today));

        var reported = await response.ReadAsync<BugReportedDto>(HttpStatusCode.Created);
        reported.Bug.WithinWarranty.Should().BeFalse();
        reported.Bug.IsDeductible.Should().BeFalse();
        reported.Bug.TaskStatus.Should().Be(WorkTaskStatus.Reopened);
    }

    [Fact]
    public async Task Employees_cannot_report_bugs()
    {
        var (task, _, owner, _) = await DeliveredTaskWithTwoOwnersAsync(completedDaysAgo: 1);
        var client = await Api.ClientForAsync(owner);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.Low, "Self report", Clock.Today));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reporting_on_a_task_that_is_not_delivered_is_a_409()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var qa = await Data.EmployeeAsync(Role.QA);
        var task = await Data.TaskAsync(45, owner);
        var client = await Api.ClientForAsync(qa);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.Low, "Too early", Clock.Today));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "bug.task_not_delivered");
    }

    [Fact]
    public async Task Bug_report_input_is_validated()
    {
        var (task, qa, _, _) = await DeliveredTaskWithTwoOwnersAsync(completedDaysAgo: 1);
        var client = await Api.ClientForAsync(qa);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest((BugSeverity)99, "", default));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(["Severity", "Description", "FoundDate"]);
    }

    [Fact]
    public async Task Fix_verify_close_flow_ends_with_the_task_closed()
    {
        var (task, qa, owner, _) = await DeliveredTaskWithTwoOwnersAsync(completedDaysAgo: 3);
        var manager = await Data.EmployeeAsync(Role.Manager);
        var qaClient = await Api.ClientForAsync(qa);
        var ownerClient = await Api.ClientForAsync(owner);
        var bug = (await (await qaClient.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.Medium, "Wrong currency", Clock.Today)))
            .ReadAsync<BugReportedDto>(HttpStatusCode.Created)).Bug;

        (await ownerClient.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.UnderFix, "On it"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ownerClient.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.Completed, "Fixed"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var verified = await (await qaClient.PostAsync(new Uri($"/api/v1/bugs/{bug.Id}/verify", UriKind.Relative), null)).ReadAsync<BugDto>();
        var closed = await (await (await Api.ClientForAsync(manager)).PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.Closed, "Verified fix")))
            .ReadAsync<TaskDetailDto>();

        verified.Status.Should().Be(BugStatus.Verified);
        closed.Task.Status.Should().Be(WorkTaskStatus.Closed);
        closed.History.Select(h => h.ToStatus).Should().EndWith([WorkTaskStatus.Reopened, WorkTaskStatus.UnderFix, WorkTaskStatus.Completed, WorkTaskStatus.Closed]);
    }

    [Fact]
    public async Task Bugs_can_be_listed_by_review_state()
    {
        var (task, qa, _, _) = await DeliveredTaskWithTwoOwnersAsync(completedDaysAgo: 1);
        var client = await Api.ClientForAsync(qa);
        await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/bugs", new ReportBugRequest(BugSeverity.Low, "Unreviewed bug", Clock.Today));

        var unreviewed = await (await client.GetAsync(new Uri("/api/v1/bugs?reviewed=false", UriKind.Relative))).ReadAsync<PagedResult<BugDto>>();
        var reviewed = await (await client.GetAsync(new Uri("/api/v1/bugs?reviewed=true", UriKind.Relative))).ReadAsync<PagedResult<BugDto>>();

        unreviewed.TotalCount.Should().Be(1);
        reviewed.TotalCount.Should().Be(0);
    }

    private async Task<(WorkTask Task, Employee Qa, Employee OwnerA, Employee OwnerB)> DeliveredTaskWithTwoOwnersAsync(int completedDaysAgo)
    {
        var a = await Data.EmployeeAsync(Role.Employee);
        var b = await Data.EmployeeAsync(Role.Employee, PayMode.Hourly, 250m);
        var qa = await Data.EmployeeAsync(Role.QA);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var task = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-completedDaysAgo), lead, (a, 6m), (b, 2m));
        return (task, qa, a, b);
    }
}
