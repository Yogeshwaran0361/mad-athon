using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using ReworkGuard.Api.DevTools;
using ReworkGuard.Application.Admin;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>Project/module creation (used by scripts/seed-demo) and the Development-only clock control.</summary>
public sealed class ProjectAdminAndDevToolsTests : ApiTestBase
{
    public ProjectAdminAndDevToolsTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Admins_create_projects_and_modules_that_tasks_can_be_planned_in()
    {
        var admin = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));
        var manager = await Data.EmployeeAsync(Role.Manager);
        var leadEmployee = await Data.EmployeeAsync(Role.TeamLead, managerId: manager.Id);
        var lead = await Api.ClientForAsync(leadEmployee);
        var employee = await Data.EmployeeAsync(Role.Employee, managerId: leadEmployee.Id);

        var project = await (await admin.PostJsonAsync("/api/v1/admin/projects", new CreateProjectRequest("Mobile Banking", null))).ReadAsync<ProjectWarrantyDto>(HttpStatusCode.Created);
        project.WarrantyDays.Should().Be(45);
        var module = await (await admin.PostJsonAsync($"/api/v1/admin/projects/{project.ProjectId}/modules", new CreateModuleRequest("Login"))).ReadAsync<ModuleDto>(HttpStatusCode.Created);

        (await (await lead.GetAsync(new Uri($"/api/v1/admin/projects/{project.ProjectId}/modules", UriKind.Relative))).ReadAsync<List<ModuleDto>>())
            .Should().ContainSingle().Which.Should().Be(module);
        await (await lead.PostJsonAsync("/api/v1/tasks", new CreateTaskRequest(module.Id, "Login screen", Clock.Today.AddDays(5), [employee.Id])))
            .ShouldHaveStatusAsync(HttpStatusCode.Created);

        await (await lead.PostJsonAsync("/api/v1/admin/projects", new CreateProjectRequest("Nope", 10))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await (await admin.PostJsonAsync("/api/v1/admin/projects", new CreateProjectRequest(" ", 10))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await admin.PostJsonAsync("/api/v1/admin/projects/999999/modules", new CreateModuleRequest("Login"))).ShouldBeProblemAsync(HttpStatusCode.NotFound);

        var actions = await FreshDb().AuditEvents.Select(a => a.Action).ToListAsync();
        actions.Should().Contain(["ProjectCreated", "ModuleCreated"]);
    }

    [Fact]
    public async Task Clock_control_does_not_exist_outside_development_even_when_the_flag_is_set()
    {
        await using var api = new ApiFactory(Db.Database.GetConnectionString()!, Clock);
        api.Settings[DevToolsExtensions.ClockControlKey] = "true";
        var admin = await api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));

        await (await admin.PostJsonAsync("/api/v1/dev/clock/advance", new AdvanceClockRequest(80))).ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        await (await admin.GetAsync(new Uri("/api/v1/dev/clock", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Clock_control_in_development_is_admin_only_and_moves_forward()
    {
        await using var api = new ApiFactory(Db.Database.GetConnectionString()!, Clock) { EnvironmentName = Environments.Development };
        api.Settings[DevToolsExtensions.ClockControlKey] = "true";
        var admin = await api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));
        var manager = await api.ClientForAsync(await Data.EmployeeAsync(Role.Manager));

        await (await manager.PostJsonAsync("/api/v1/dev/clock/advance", new AdvanceClockRequest(1))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await (await admin.PostJsonAsync("/api/v1/dev/clock/advance", new AdvanceClockRequest(-1))).ShouldHaveStatusAsync(HttpStatusCode.BadRequest);

        var moved = await (await admin.PostJsonAsync("/api/v1/dev/clock/advance", new AdvanceClockRequest(80))).ReadAsync<DevClockDto>();
        moved.OffsetHours.Should().Be(80);
        moved.UtcNow.Should().BeCloseTo(DateTime.UtcNow.AddHours(80), TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Development", null, false)]
    [InlineData("Production", "true", false)]
    [InlineData("Staging", "true", false)]
    [InlineData("Testing", "true", false)]
    public void Clock_control_needs_development_and_the_flag(string environment, string? flag, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [DevToolsExtensions.ClockControlKey] = flag }).Build();
        DevToolsExtensions.IsClockControlEnabled(new FakeEnvironment(environment), configuration).Should().Be(expected);
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name) => EnvironmentName = name;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "ReworkGuard.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
