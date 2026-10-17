using Microsoft.EntityFrameworkCore;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Employee> Employees { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Project> Projects { get; }

    DbSet<Module> Modules { get; }

    DbSet<WorkTask> WorkTasks { get; }

    DbSet<TaskAssignment> TaskAssignments { get; }

    DbSet<TaskStateHistory> TaskStateHistory { get; }

    DbSet<Bug> Bugs { get; }

    DbSet<TimeLog> TimeLogs { get; }

    DbSet<Deduction> Deductions { get; }

    DbSet<AuditEvent> AuditEvents { get; }

    DbSet<PolicySetting> PolicySettings { get; }

    DbSet<AttributionShareOverride> AttributionShareOverrides { get; }

    DbSet<DeductionDispute> DeductionDisputes { get; }

    DbSet<Holiday> Holidays { get; }

    DbSet<EmployeeRateHistory> EmployeeRateHistory { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<NotificationPreference> NotificationPreferences { get; }

    DbSet<PayrollPeriod> PayrollPeriods { get; }

    DbSet<PayrollRun> PayrollRuns { get; }

    DbSet<PayrollRunEmployee> PayrollRunEmployees { get; }

    DbSet<PayrollRunItem> PayrollRunItems { get; }

    DbSet<ChangeRequest> ChangeRequests { get; }

    DbSet<IncentiveAward> IncentiveAwards { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Takes a row lock on the employee until the current transaction ends, serialising per-employee checks.</summary>
    Task LockEmployeeAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>Creates the month's <see cref="PayrollPeriod"/> row if missing. Call outside any transaction, before locking it.</summary>
    Task EnsurePayrollPeriodAsync(string month, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the month's existing <see cref="PayrollPeriod"/> row until the current transaction ends, serialising run
    /// generation and finalization for that month.
    /// </summary>
    Task LockPayrollPeriodAsync(string month, CancellationToken cancellationToken);

    /// <summary>
    /// Ids of pending outbox messages that are due, locked until the current transaction ends. Rows another worker
    /// has locked are skipped, so several workers can drain the outbox side by side.
    /// </summary>
    Task<IReadOnlyList<long>> LockDueOutboxMessagesAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken);
}
