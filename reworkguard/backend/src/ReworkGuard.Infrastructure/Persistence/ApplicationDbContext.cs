using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Module> Modules => Set<Module>();

    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

    public DbSet<TaskStateHistory> TaskStateHistory => Set<TaskStateHistory>();

    public DbSet<Bug> Bugs => Set<Bug>();

    public DbSet<TimeLog> TimeLogs => Set<TimeLog>();

    public DbSet<Deduction> Deductions => Set<Deduction>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<PolicySetting> PolicySettings => Set<PolicySetting>();

    public DbSet<AttributionShareOverride> AttributionShareOverrides => Set<AttributionShareOverride>();

    public DbSet<DeductionDispute> DeductionDisputes => Set<DeductionDispute>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<EmployeeRateHistory> EmployeeRateHistory => Set<EmployeeRateHistory>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();

    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();

    public DbSet<IncentiveAward> IncentiveAwards => Set<IncentiveAward>();

    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();

    public DbSet<PayrollRunEmployee> PayrollRunEmployees => Set<PayrollRunEmployee>();

    public DbSet<PayrollRunItem> PayrollRunItems => Set<PayrollRunItem>();

    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfApplicationTransaction(await Database.BeginTransactionAsync(cancellationToken));

    public async Task LockEmployeeAsync(int employeeId, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Employee locks require an open transaction.");
        }

        await Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Employees WHERE Id = {employeeId} FOR UPDATE", cancellationToken);
    }

    public async Task LockPayrollPeriodAsync(string month, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Payroll period locks require an open transaction.");
        }

        // Only an existing row is locked: locking a missing row takes gap locks, and two such sessions inserting it deadlock.
        var locked = await Database.SqlQuery<string>($"SELECT Month AS Value FROM PayrollPeriods WHERE Month = {month} FOR UPDATE").ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            throw new InvalidOperationException($"Payroll period {month} does not exist; call EnsurePayrollPeriodAsync before the transaction.");
        }
    }

    public async Task EnsurePayrollPeriodAsync(string month, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Create the payroll period row before opening the transaction that locks it.");
        }

        await Database.ExecuteSqlInterpolatedAsync($"INSERT IGNORE INTO PayrollPeriods (Month, IsLocked, Version) VALUES ({month}, FALSE, 0)", cancellationToken);
    }

    public async Task<IReadOnlyList<long>> LockDueOutboxMessagesAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Outbox locks require an open transaction.");
        }

        return await Database
            .SqlQuery<long>($"SELECT Id AS Value FROM OutboxMessages WHERE Status = 'Pending' AND NextAttemptAtUtc <= {utcNow} ORDER BY Id LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyWriteRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HavePrecision(6);
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>().HavePrecision(6);

        configurationBuilder.Properties<Role>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PayMode>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<WorkTaskStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<BugSeverity>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<BugStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RootCause>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<WorkType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ApprovalStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DeductionStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DeductionKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DisputeOutcome>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<NotificationType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<NotificationChannel>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<OutboxStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PayrollRunStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PayrollItemKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ChangeRequestStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<IncentiveAwardStatus>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    private void ApplyWriteRules()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IAppendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} records are append-only and cannot be changed or deleted.");
            }

            if (entry.Entity is IVersioned versioned && entry.State == EntityState.Modified)
            {
                versioned.Version++;
            }
        }
    }
}
