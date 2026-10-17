using System.Net;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class TaskApiTests : ApiTestBase
{
    public TaskApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Each_transition_writes_history_and_an_audit_event()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var task = await Data.TaskAsync(45, owner);
        var client = await Api.ClientForAsync(owner);

        var started = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.InProgress, "Starting"));
        var done = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.Completed, null));

        (await started.ReadAsync<TaskDetailDto>()).Task.Status.Should().Be(WorkTaskStatus.InProgress);
        var detail = await done.ReadAsync<TaskDetailDto>();
        detail.Task.Status.Should().Be(WorkTaskStatus.Completed);
        detail.Task.ActualCompletionDate.Should().Be(Clock.Today);
        detail.History.Select(h => h.ToStatus).Should().Equal(WorkTaskStatus.InProgress, WorkTaskStatus.Completed);
        detail.History[0].ChangedByName.Should().Be(owner.Name);
        detail.AllowedTransitions.Should().Equal(WorkTaskStatus.Closed);

        await using var db = FreshDb();
        var audits = await db.AuditEvents.Where(a => a.EntityType == "Task" && a.EntityId == task.Id.ToString()).ToListAsync();
        audits.Should().HaveCount(2).And.OnlyContain(a => a.Action == "StatusChanged" && a.ActorEmployeeId == owner.Id);
        audits[1].DetailsJson.Should().Contain("\"toStatus\": \"Completed\"");
    }

    [Fact]
    public async Task An_illegal_transition_returns_409_problem_details_and_changes_nothing()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var task = await Data.TaskAsync(45, owner);
        var client = await Api.ClientForAsync(owner);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.Completed, null));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "task.invalid_transition");
        problem.GetProperty("detail").GetString().Should().Contain("Assigned to Completed");
        await using var db = FreshDb();
        (await db.TaskStateHistory.CountAsync()).Should().Be(0);
        (await db.AuditEvents.CountAsync(e => e.EntityType != "Employee")).Should().Be(0, "sign-ins are the only audited events");
    }

    [Fact]
    public async Task Reopening_by_hand_is_rejected_with_409()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var task = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-2), lead, (owner, 4m));
        var client = await Api.ClientForAsync(lead);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.Reopened, null));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "task.invalid_transition");
        problem.GetProperty("detail").GetString().Should().Contain("reporting a bug");
    }

    [Fact]
    public async Task An_employee_cannot_move_someone_elses_task()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var other = await Data.EmployeeAsync(Role.Employee);
        var task = await Data.TaskAsync(45, owner);
        var client = await Api.ClientForAsync(other);

        var response = await client.PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", new TransitionTaskRequest(WorkTaskStatus.InProgress, null));

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Closing_inside_the_warranty_window_needs_a_manager()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var manager = await Data.EmployeeAsync(Role.Manager);
        var task = await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-2), lead, (owner, 4m));
        var close = new TransitionTaskRequest(WorkTaskStatus.Closed, "Customer signed off");

        var byOwner = await (await Api.ClientForAsync(owner)).PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", close);
        var byLead = await (await Api.ClientForAsync(lead)).PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", close);
        var byManager = await (await Api.ClientForAsync(manager)).PostJsonAsync($"/api/v1/tasks/{task.Id}/transitions", close);

        await byOwner.ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await byLead.ShouldBeProblemAsync(HttpStatusCode.Conflict, "task.invalid_transition");
        (await byManager.ReadAsync<TaskDetailDto>()).Task.Status.Should().Be(WorkTaskStatus.Closed);
    }

    [Fact]
    public async Task A_team_lead_creates_a_task_with_original_deliverers()
    {
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var a = await Data.EmployeeAsync(Role.Employee);
        var b = await Data.EmployeeAsync(Role.Employee);
        var existing = await Data.TaskAsync(45, a);
        var client = await Api.ClientForAsync(lead);

        var response = await client.PostJsonAsync("/api/v1/tasks", new CreateTaskRequest(existing.ModuleId, "New screen", new DateOnly(2026, 4, 1), [a.Id, b.Id]));

        var created = await response.ReadAsync<TaskSummaryDto>(HttpStatusCode.Created);
        created.Status.Should().Be(WorkTaskStatus.Assigned);
        created.Assignees.Should().HaveCount(2).And.OnlyContain(x => x.IsOriginalDeliverer);
        response.Headers.Location!.ToString().Should().EndWith($"/api/v1/tasks/{created.Id}");
        await using var db = FreshDb();
        (await db.AuditEvents.SingleAsync(e => e.EntityType != "Employee")).Action.Should().Be("Created");
    }

    [Fact]
    public async Task Creating_a_task_validates_input_and_role()
    {
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        var employee = await Data.EmployeeAsync(Role.Employee);
        var existing = await Data.TaskAsync(45, employee);

        var invalid = await (await Api.ClientForAsync(lead)).PostJsonAsync("/api/v1/tasks", new CreateTaskRequest(existing.ModuleId, "", new DateOnly(2026, 4, 1), []));
        var forbidden = await (await Api.ClientForAsync(employee)).PostJsonAsync("/api/v1/tasks", new CreateTaskRequest(existing.ModuleId, "X", new DateOnly(2026, 4, 1), [employee.Id]));

        var problem = await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(["Title", "AssigneeIds"]);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Task_list_is_paginated_and_filterable()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var lead = await Data.EmployeeAsync(Role.TeamLead);
        await Data.TaskAsync(45, owner);
        await Data.TaskAsync(45, owner);
        await Data.DeliveredTaskAsync(45, Clock.UtcNow.AddDays(-1), lead, (owner, 2m));
        var client = await Api.ClientForAsync(owner);

        var firstPage = await (await client.GetAsync(new Uri("/api/v1/tasks?pageSize=2", UriKind.Relative))).ReadAsync<PagedResult<TaskSummaryDto>>();
        var completed = await (await client.GetAsync(new Uri("/api/v1/tasks?status=Completed", UriKind.Relative))).ReadAsync<PagedResult<TaskSummaryDto>>();
        var mine = await (await client.GetAsync(new Uri($"/api/v1/tasks?assigneeId={owner.Id}", UriKind.Relative))).ReadAsync<PagedResult<TaskSummaryDto>>();

        firstPage.Items.Should().HaveCount(2);
        firstPage.TotalCount.Should().Be(3);
        firstPage.TotalPages.Should().Be(2);
        completed.Items.Should().ContainSingle().Which.Status.Should().Be(WorkTaskStatus.Completed);
        mine.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Unknown_task_returns_404()
    {
        var owner = await Data.EmployeeAsync(Role.Employee);
        var client = await Api.ClientForAsync(owner);

        var response = await client.GetAsync(new Uri("/api/v1/tasks/999999", UriKind.Relative));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound);
    }
}
