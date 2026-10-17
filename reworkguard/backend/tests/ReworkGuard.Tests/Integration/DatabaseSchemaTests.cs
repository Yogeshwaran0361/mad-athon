using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Auth;
using ReworkGuard.Infrastructure.Persistence;
using ReworkGuard.Infrastructure.Persistence.Seeding;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

[Collection(MySqlTestGroup.Name)]
public sealed class DatabaseSchemaTests : IAsyncLifetime
{
    private readonly MySqlContainerFixture _mysql;
    private ApplicationDbContext _db = null!;

    public DatabaseSchemaTests(MySqlContainerFixture mysql)
    {
        _mysql = mysql;
    }

    public async Task InitializeAsync() =>
        _db = MySqlContainerFixture.CreateContext(await _mysql.CreateMigratedDatabaseAsync());

    public Task DisposeAsync() => MySqlContainerFixture.DropDatabaseAsync(_db);

    [Fact]
    public async Task Migrations_apply_to_a_fresh_database_and_seed_the_policy_defaults()
    {
        (await _db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        var policies = await _db.PolicySettings.ToDictionaryAsync(p => p.Key, p => p.Value);
        policies.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["StandardHoursPerMonth"] = "160",
            ["DailyHours"] = "8",
            ["DeductionCapPercent"] = "20",
            ["DefaultWarrantyDays"] = "45",
            ["DisputeWindowDays"] = "3",
            ["QaMissDeductible"] = "false",
            ["QaMissSharePercent"] = "50",
            ["RequireEmployeeAcknowledgement"] = "false",
            ["ProfitSharePercent"] = "20",
            ["LabourOverheadFactor"] = "1.5",
        });
    }

    [Fact]
    public async Task Seeder_creates_the_demo_roles_pay_modes_project_and_tasks()
    {
        var seeder = new DbSeeder(
            _db,
            new IdentityPasswordHasher(),
            new TestClock(),
            Options.Create(new DatabaseOptions { SeedPassword = "demo-password" }),
            NullLogger<DbSeeder>.Instance);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        var employees = await _db.Employees.ToListAsync();
        employees.Select(e => e.Role).Should().Contain([Role.Admin, Role.Manager, Role.TeamLead, Role.QA, Role.Finance]);
        employees.Where(e => e.Role == Role.Employee).Select(e => e.PayMode)
            .Should().BeEquivalentTo([PayMode.Monthly, PayMode.Daily, PayMode.Hourly]);
        (await _db.Projects.CountAsync()).Should().Be(1);
        (await _db.Modules.CountAsync()).Should().Be(2);
        (await _db.WorkTasks.CountAsync()).Should().Be(4);
    }

    [Theory]
    [InlineData("AuditEvents", "UPDATE AuditEvents SET Action = 'tampered'")]
    [InlineData("AuditEvents", "DELETE FROM AuditEvents")]
    [InlineData("TaskStateHistory", "UPDATE TaskStateHistory SET Reason = 'tampered'")]
    [InlineData("TaskStateHistory", "DELETE FROM TaskStateHistory")]
    public async Task Append_only_tables_reject_updates_and_deletes_at_the_database(string table, string sql)
    {
        var data = new TestData(_db);
        var employee = await data.EmployeeAsync(Role.Employee);
        var task = await data.TaskAsync(45, employee);
        task.MoveTo(WorkTaskStatus.InProgress, employee.Id, "start", DateTime.UtcNow);
        _db.AuditEvents.Add(new AuditEvent(DateTime.UtcNow, employee.Id, "Task", task.Id.ToString(CultureInfo.InvariantCulture), "Started", "{}"));
        await _db.SaveChangesAsync();

        var act = () => _db.Database.ExecuteSqlRawAsync(sql);

        (await act.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain($"{table} is append-only");
    }

    [Theory]
    [InlineData("Corrective", false, 2.00)]
    [InlineData("Regular", true, 2.00)]
    [InlineData("Regular", false, 0.00)]
    [InlineData("Regular", false, 24.50)]
    public async Task TimeLog_check_constraints_reject_invalid_rows(string workType, bool withBug, double hoursValue)
    {
        var data = new TestData(_db);
        var employee = await data.EmployeeAsync(Role.Employee);
        var task = await data.DeliveredTaskAsync(45, DateTime.UtcNow.AddDays(-1), employee, (employee, 0m));
        var bug = task.ReportBug(45, BugSeverity.Low, "Broken", DateOnly.FromDateTime(DateTime.UtcNow), employee.Id, DateTime.UtcNow);
        await _db.SaveChangesAsync();
        int? bugId = withBug ? bug.Id : null;
        var hours = (decimal)hoursValue;

        var act = () => _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TimeLogs (TaskId, EmployeeId, WorkDate, Hours, WorkType, BugId, ApprovalStatus, CreatedAtUtc, Version) VALUES ({task.Id}, {employee.Id}, '2026-01-10', {hours}, {workType}, {bugId}, 'Pending', UTC_TIMESTAMP(), 0)");

        (await act.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain("CK_TimeLogs");
    }

    [Fact]
    public async Task Task_assignment_is_unique_per_task_and_employee()
    {
        var data = new TestData(_db);
        var employee = await data.EmployeeAsync(Role.Employee);
        var task = await data.TaskAsync(45, employee);

        var act = () => _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO TaskAssignments (TaskId, EmployeeId, IsOriginalDeliverer) VALUES ({task.Id}, {employee.Id}, 0)");

        (await act.Should().ThrowAsync<MySqlException>()).Which.Message.Should().Contain("Duplicate entry");
    }
}
