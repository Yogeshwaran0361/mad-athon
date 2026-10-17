using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Auth;
using ReworkGuard.Infrastructure.Persistence;

namespace ReworkGuard.Tests.Support;

/// <summary>Builds a small, known data set directly through the domain model.</summary>
public sealed class TestData
{
    public const string Password = "Correct-Horse-9";

    private static readonly string PasswordHash = new IdentityPasswordHasher().Hash(Password);

    private readonly ApplicationDbContext _db;
    private int _sequence;

    public TestData(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Employee> EmployeeAsync(Role role, PayMode payMode = PayMode.Monthly, decimal rate = 48000m, int? managerId = null)
    {
        var n = Interlocked.Increment(ref _sequence);
        var employee = new Employee(
            $"T{n:D4}",
            $"{role} {n}",
            $"{role.ToString().ToLowerInvariant()}{n}@test.local",
            role,
            payMode,
            payMode == PayMode.Monthly ? rate : null,
            payMode == PayMode.Daily ? rate : null,
            payMode == PayMode.Hourly ? rate : null,
            managerId);
        employee.SetPasswordHash(PasswordHash);
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return employee;
    }

    public async Task<WorkTask> TaskAsync(int warrantyDays, params Employee[] deliverers)
    {
        var project = new Project($"Project {Interlocked.Increment(ref _sequence)}", warrantyDays);
        var module = new Module(project, "Module A");
        var task = new WorkTask(module, "Task under test", new DateOnly(2026, 1, 31), deliverers.Select(d => d.Id));
        _db.AddRange(project, module, task);
        await _db.SaveChangesAsync();
        return task;
    }

    /// <summary>Moves a task through InProgress to Completed, logging and approving the given regular hours per deliverer.</summary>
    public async Task<WorkTask> DeliveredTaskAsync(
        int warrantyDays,
        DateTime completedAtUtc,
        Employee approver,
        params (Employee Deliverer, decimal RegularHours)[] work)
    {
        var task = await TaskAsync(warrantyDays, work.Select(w => w.Deliverer).ToArray());
        var started = completedAtUtc.AddDays(-5);
        task.MoveTo(WorkTaskStatus.InProgress, work[0].Deliverer.Id, "start", started);
        foreach (var (deliverer, hours) in work.Where(w => w.RegularHours > 0))
        {
            var log = task.LogRegularHours(deliverer.Id, DateOnly.FromDateTime(started), hours, null, 0m, started);
            log.Approve(approver.Id, started);
            _db.TimeLogs.Add(log);
        }

        task.MoveTo(WorkTaskStatus.Completed, work[0].Deliverer.Id, "done", completedAtUtc);
        await _db.SaveChangesAsync();
        return task;
    }
}
