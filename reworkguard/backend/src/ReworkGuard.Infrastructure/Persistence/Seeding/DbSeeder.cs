using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds demo data into an empty database: one user per staff role, three employees on different pay modes,
/// one project with two modules and four tasks in different lifecycle states.
/// </summary>
public sealed partial class DbSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly DatabaseOptions _options;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(ApplicationDbContext db, IPasswordHasher hasher, IClock clock, IOptions<DatabaseOptions> options, ILogger<DbSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await _db.Employees.AnyAsync(cancellationToken))
        {
            LogSkipped(_logger);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SeedPassword) || _options.SeedPassword.Length < 8)
        {
            throw new InvalidOperationException("Database:SeedPassword must be set (at least 8 characters) to seed demo users.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = _clock.UtcNow;
        var passwordHash = _hasher.Hash(_options.SeedPassword);

        var manager = Add(new Employee("MGR001", "Maya Manager", "manager@reworkguard.local", Role.Manager, PayMode.Monthly, 120000m, null, null, null), passwordHash);
        await _db.SaveChangesAsync(cancellationToken);

        Add(new Employee("ADM001", "Arun Admin", "admin@reworkguard.local", Role.Admin, PayMode.Monthly, 90000m, null, null, manager.Id), passwordHash);
        var lead = Add(new Employee("TL001", "Tara Lead", "lead@reworkguard.local", Role.TeamLead, PayMode.Monthly, 96000m, null, null, manager.Id), passwordHash);
        Add(new Employee("QA001", "Quinn Tester", "qa@reworkguard.local", Role.QA, PayMode.Monthly, 64000m, null, null, manager.Id), passwordHash);
        Add(new Employee("FIN001", "Farah Finance", "finance@reworkguard.local", Role.Finance, PayMode.Monthly, 85000m, null, null, manager.Id), passwordHash);
        await _db.SaveChangesAsync(cancellationToken);

        var monthly = Add(new Employee("EMP001", "Priya Monthly", "priya@reworkguard.local", Role.Employee, PayMode.Monthly, 48000m, null, null, lead.Id), passwordHash);
        var daily = Add(new Employee("EMP002", "Dev Daily", "dev@reworkguard.local", Role.Employee, PayMode.Daily, null, 2400m, null, lead.Id), passwordHash);
        var hourly = Add(new Employee("EMP003", "Hana Hourly", "hana@reworkguard.local", Role.Employee, PayMode.Hourly, null, null, 275m, lead.Id), passwordHash);
        await _db.SaveChangesAsync(cancellationToken);

        var project = new Project("Customer Portal");
        var checkout = new Module(project, "Checkout");
        var invoicing = new Module(project, "Invoicing");
        _db.AddRange(project, checkout, invoicing);

        var cardForm = new WorkTask(checkout, "Card payment form", DateOnly.FromDateTime(now.AddDays(-12)), [monthly.Id, daily.Id]);
        var refundApi = new WorkTask(checkout, "Refund request API", DateOnly.FromDateTime(now.AddDays(5)), [hourly.Id]);
        var numbering = new WorkTask(invoicing, "Invoice numbering", DateOnly.FromDateTime(now.AddDays(10)), [monthly.Id]);
        var pdfExport = new WorkTask(invoicing, "Invoice PDF export", DateOnly.FromDateTime(now.AddDays(-70)), [hourly.Id]);
        _db.WorkTasks.AddRange(cardForm, refundApi, numbering, pdfExport);
        await _db.SaveChangesAsync(cancellationToken);

        // Card payment form: delivered 12 days ago, inside the 45-day warranty window. Ready for a bug report.
        cardForm.MoveTo(WorkTaskStatus.InProgress, monthly.Id, "Started", now.AddDays(-16));
        var cardLogs = new[]
        {
            cardForm.LogRegularHours(monthly.Id, DateOnly.FromDateTime(now.AddDays(-15)), 8m, "Form layout", 0m, now.AddDays(-15)),
            cardForm.LogRegularHours(monthly.Id, DateOnly.FromDateTime(now.AddDays(-14)), 8m, "Validation", 0m, now.AddDays(-14)),
            cardForm.LogRegularHours(daily.Id, DateOnly.FromDateTime(now.AddDays(-14)), 8m, "Gateway integration", 0m, now.AddDays(-14)),
        };
        cardForm.MoveTo(WorkTaskStatus.Completed, monthly.Id, "Delivered", now.AddDays(-12));

        // Refund request API: in progress, with one regular log waiting in the lead's approval queue.
        refundApi.MoveTo(WorkTaskStatus.InProgress, hourly.Id, "Started", now.AddDays(-2));
        var refundLog = refundApi.LogRegularHours(hourly.Id, DateOnly.FromDateTime(now.AddDays(-1)), 6m, "Endpoint skeleton", 0m, now.AddDays(-1));

        // Invoice PDF export: delivered 70 days ago, so its warranty window has passed.
        pdfExport.MoveTo(WorkTaskStatus.InProgress, hourly.Id, "Started", now.AddDays(-75));
        var pdfLog = pdfExport.LogRegularHours(hourly.Id, DateOnly.FromDateTime(now.AddDays(-74)), 7.5m, "PDF renderer", 0m, now.AddDays(-74));
        pdfExport.MoveTo(WorkTaskStatus.Completed, hourly.Id, "Delivered", now.AddDays(-70));

        foreach (var log in cardLogs.Append(pdfLog))
        {
            log.Approve(lead.Id, now.AddDays(-10));
        }

        _db.TimeLogs.AddRange(cardLogs);
        _db.TimeLogs.AddRange(refundLog, pdfLog);
        _db.AuditEvents.Add(new AuditEvent(now, null, "System", "seed", "DemoDataSeeded", "{\"employees\":8,\"tasks\":4}"));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogSeeded(_logger);
    }

    private Employee Add(Employee employee, string passwordHash)
    {
        employee.SetPasswordHash(passwordHash);
        _db.Employees.Add(employee);
        return employee;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database already has data; seeding skipped.")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded demo data.")]
    private static partial void LogSeeded(ILogger logger);
}
