using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Admin;
using ReworkGuard.Application.Auth;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

public sealed class AdminApiTests : ApiTestBase
{
    private static readonly PolicyDto Defaults = new(160m, 8m, 20m, 45, 3, false, 50m, false);

    public AdminApiTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Admins_change_policy_and_the_change_is_audited_with_before_and_after()
    {
        var admin = await AdminAsync();

        (await (await admin.GetAsync(new Uri("/api/v1/admin/policies", UriKind.Relative))).ReadAsync<PolicyDto>()).Should().Be(Defaults);

        var changed = Defaults with { DeductionCapPercent = 15m, DisputeWindowDays = 5, QaMissDeductible = true, QaMissSharePercent = 40m };
        (await (await admin.PutJsonAsync("/api/v1/admin/policies", changed)).ReadAsync<PolicyDto>()).Should().Be(changed);
        (await (await admin.GetAsync(new Uri("/api/v1/admin/policies", UriKind.Relative))).ReadAsync<PolicyDto>()).Should().Be(changed);

        var audit = await FreshDb().AuditEvents.SingleAsync(a => a.Action == "PolicyChanged");
        using var details = System.Text.Json.JsonDocument.Parse(audit.DetailsJson);
        details.RootElement.GetProperty("before").GetProperty("deductionCapPercent").GetDecimal().Should().Be(20m);
        details.RootElement.GetProperty("after").GetProperty("deductionCapPercent").GetDecimal().Should().Be(15m);
    }

    [Theory]
    [InlineData(0, 3, 50)]
    [InlineData(20, 0, 50)]
    [InlineData(20, 31, 50)]
    [InlineData(101, 3, 50)]
    [InlineData(20, 3, 100.5)]
    public async Task Invalid_policy_values_are_rejected(double cap, int window, double qaShare)
    {
        var admin = await AdminAsync();

        await (await admin.PutJsonAsync("/api/v1/admin/policies", Defaults with { DeductionCapPercent = (decimal)cap, DisputeWindowDays = window, QaMissSharePercent = (decimal)qaShare, StandardHoursPerMonth = cap == 0 ? 0 : 160m }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_admins_change_settings_and_only_finance_and_admin_see_pay()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);

        foreach (var client in new[] { s.Manager, s.Finance, s.Lead, s.PriyaClient })
        {
            await (await client.PutJsonAsync("/api/v1/admin/policies", Defaults)).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
            await (await client.PostJsonAsync("/api/v1/admin/holidays", new CreateHolidayRequest(new DateOnly(2026, 8, 15), "Independence Day"))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        }

        await (await s.Finance.GetAsync(new Uri("/api/v1/admin/employees", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        await (await s.Manager.GetAsync(new Uri("/api/v1/admin/employees", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/admin/employees/{s.Priya.Id}/pay", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await (await s.PriyaClient.GetAsync(new Uri("/api/v1/admin/policies", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await (await s.PriyaClient.GetAsync(new Uri("/api/v1/admin/holidays", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_project_warranty_change_applies_to_bugs_reported_afterwards()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock, warrantyDays: 5);
        var admin = await AdminAsync();
        var projectId = (await FreshDb().WorkTasks.Include(t => t.Module).SingleAsync(t => t.Id == s.TaskId)).Module.ProjectId;

        // Delivered 9 days ago with a 5-day warranty: outside. After the change to 30 days: inside.
        await (await admin.PutJsonAsync($"/api/v1/admin/projects/{projectId}/warranty", new UpdateWarrantyRequest(30))).ShouldHaveStatusAsync(HttpStatusCode.OK);
        var reported = await (await s.Qa.PostJsonAsync($"/api/v1/tasks/{s.TaskId}/bugs", new ReportBugRequest(BugSeverity.Low, "Typo in the receipt", Clock.Today)))
            .ReadAsync<BugReportedDto>(HttpStatusCode.Created);

        reported.Bug.WithinWarranty.Should().BeTrue();
        await (await admin.PutJsonAsync($"/api/v1/admin/projects/{projectId}/warranty", new UpdateWarrantyRequest(-1))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        (await FreshDb().AuditEvents.CountAsync(a => a.Action == "WarrantyChanged")).Should().Be(1);
    }

    [Fact]
    public async Task Holidays_extend_dispute_windows_opened_after_they_are_added()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var admin = await AdminAsync();
        await s.DeductibleBugAsync();
        var lines = await ReworkScenario.LinesAsync(s.Manager);

        // Tuesday 10 March + 3 working days = Friday 13 March. With Thursday 12 March a holiday: Monday 16 March.
        var holiday = await (await admin.PostJsonAsync("/api/v1/admin/holidays", new CreateHolidayRequest(new DateOnly(2026, 3, 12), "Company day off")))
            .ReadAsync<HolidayDto>(HttpStatusCode.Created);
        await (await admin.PostJsonAsync("/api/v1/admin/holidays", new CreateHolidayRequest(new DateOnly(2026, 3, 12), "Duplicate")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "holiday.exists");

        var notified = await s.NotifyAsync(lines[0].Id);
        notified.Single().DisputeWindowEndsAtUtc.Should().Be(new DateTime(2026, 3, 16, 18, 30, 0, DateTimeKind.Utc).AddTicks(-10));

        (await (await s.PriyaClient.GetAsync(new Uri("/api/v1/admin/holidays?year=2026", UriKind.Relative))).ReadAsync<IReadOnlyList<HolidayDto>>())
            .Should().ContainSingle().Which.Name.Should().Be("Company day off");

        await (await admin.DeleteAsync(new Uri($"/api/v1/admin/holidays/{holiday.Id}", UriKind.Relative))).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        var after = await s.NotifyAsync(lines[1].Id);
        after.Single().DisputeWindowEndsAtUtc.Should().Be(new DateTime(2026, 3, 13, 18, 30, 0, DateTimeKind.Utc).AddTicks(-10));
        var kept = await (await s.Manager.GetAsync(new Uri($"/api/v1/deductions/{lines[0].Id}", UriKind.Relative))).ReadAsync<DeductionDto>();
        kept.DisputeWindowEndsAtUtc.Should().Be(notified.Single().DisputeWindowEndsAtUtc, "windows already opened keep their end");
    }

    [Fact]
    public async Task Admins_create_users_change_roles_and_record_pay_history()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var admin = await AdminAsync();

        var created = await (await admin.PostJsonAsync("/api/v1/admin/employees", new CreateEmployeeRequest(
                "emp-900", "Ravi Kumar", "Ravi@Example.test", Role.Employee, s.LeadEmployee.Id, PayMode.Monthly, 60000m, null, null, new DateOnly(2026, 1, 1), "Welcome-2026-pass")))
            .ReadAsync<EmployeeAdminDto>(HttpStatusCode.Created);
        created.Should().BeEquivalentTo(new { Code = "EMP-900", Email = "ravi@example.test", MonthlyGross = 60000m, IsActive = true });

        await (await admin.PostJsonAsync("/api/v1/admin/employees", new CreateEmployeeRequest(
                "EMP-901", "Duplicate", "ravi@example.test", Role.Employee, null, PayMode.Monthly, 1m, null, null, new DateOnly(2026, 1, 1), "Welcome-2026-pass")))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "employee.duplicate");
        await (await admin.PostJsonAsync("/api/v1/admin/employees", new CreateEmployeeRequest(
                "EMP-902", "Weak", "weak@example.test", Role.Employee, null, PayMode.Monthly, 1m, null, null, new DateOnly(2026, 1, 1), "short")))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        var login = Api.CreateClient();
        (await login.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ravi@example.test", "Welcome-2026-pass"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await (await admin.PostJsonAsync($"/api/v1/admin/employees/{created.Id}/pay", new ChangePayRequest(PayMode.Hourly, null, null, 400m, new DateOnly(2026, 3, 1))))
            .ReadAsync<IReadOnlyList<RateHistoryDto>>();
        history.Select(h => (h.EffectiveFrom, h.PayMode)).Should().Equal((new DateOnly(2026, 3, 1), PayMode.Hourly), (new DateOnly(2026, 1, 1), PayMode.Monthly));
        await (await admin.PostJsonAsync($"/api/v1/admin/employees/{created.Id}/pay", new ChangePayRequest(PayMode.Hourly, null, null, 410m, new DateOnly(2026, 3, 1))))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "employee.rate_exists");
        await (await admin.PostJsonAsync($"/api/v1/admin/employees/{created.Id}/pay", new ChangePayRequest(PayMode.Hourly, 1m, null, null, new DateOnly(2026, 4, 1))))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest);

        var updated = await (await admin.PutJsonAsync($"/api/v1/admin/employees/{created.Id}", new UpdateEmployeeRequest("Ravi K.", Role.TeamLead, s.ManagerEmployee.Id, false)))
            .ReadAsync<EmployeeAdminDto>();
        updated.Should().BeEquivalentTo(new { Name = "Ravi K.", Role = Role.TeamLead, IsActive = false, PayMode = PayMode.Hourly, HourlyRate = 400m });
        (await login.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ravi@example.test", "Welcome-2026-pass"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var page = await (await s.Finance.GetAsync(new Uri("/api/v1/admin/employees?includeInactive=true&search=Ravi", UriKind.Relative))).ReadAsync<PagedResult<EmployeeAdminDto>>();
        page.Items.Should().ContainSingle().Which.Id.Should().Be(created.Id);
        var actions = await FreshDb().AuditEvents.Where(a => a.EntityType == "Employee" && a.EntityId == created.Id.ToString()).OrderBy(a => a.Sequence).Select(a => a.Action).ToListAsync();
        actions.Should().Equal("Created", "LoggedIn", "PayChanged", "Updated", "LoginRejected");
        (await FreshDb().AuditEvents.AnyAsync(a => a.DetailsJson.Contains("Welcome-2026-pass"))).Should().BeFalse("passwords never reach the audit trail");
    }

    [Fact]
    public async Task Admins_cannot_lock_themselves_out_and_can_reset_passwords()
    {
        var adminEmployee = await Data.EmployeeAsync(Role.Admin);
        var admin = await Api.ClientForAsync(adminEmployee);
        var priya = await Data.EmployeeAsync(Role.Employee);

        await (await admin.PutJsonAsync($"/api/v1/admin/employees/{adminEmployee.Id}", new UpdateEmployeeRequest("Me", Role.Admin, null, false)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "employee.self_change");
        await (await admin.PutJsonAsync($"/api/v1/admin/employees/{priya.Id}", new UpdateEmployeeRequest("P", Role.Employee, priya.Id, true)))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "employee.self_manager");

        await (await admin.PostJsonAsync($"/api/v1/admin/employees/{priya.Id}/password", new ResetPasswordRequest("Brand-new-pass-42"))).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await Api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(priya.Email, "Brand-new-pass-42"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> AdminAsync() => await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));
}
