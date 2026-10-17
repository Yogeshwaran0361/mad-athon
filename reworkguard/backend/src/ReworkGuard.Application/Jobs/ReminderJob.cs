using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Jobs;

/// <summary>
/// Daily nudges: approvers with time logs pending for more than a day, and managers with deduction lines left in
/// Draft for more than two days. Each recipient gets at most one of each per IST day, however often the job runs.
/// </summary>
public sealed class ReminderJob
{
    public static readonly TimeSpan PendingLogAge = TimeSpan.FromDays(1);
    public static readonly TimeSpan DraftAge = TimeSpan.FromDays(2);

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly NotificationPublisher _notifications;

    public ReminderJob(IApplicationDbContext db, IClock clock, NotificationPublisher notifications)
    {
        _db = db;
        _clock = clock;
        _notifications = notifications;
    }

    public sealed record Result(int ApproversReminded, int ManagersReminded);

    public async Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var startOfDay = DeductionWindowJob.StartOfTodayUtc(now);
        var managers = await _notifications.ActiveEmployeesInRoleAsync(Role.Manager, cancellationToken);

        // Pending logs go to the employee's team lead; employees without an active lead fall to the managers.
        var pending = await _db.TimeLogs.AsNoTracking()
            .Where(l => l.ApprovalStatus == ApprovalStatus.Pending && l.CreatedAtUtc <= now - PendingLogAge)
            .Select(l => new
            {
                l.Hours,
                Lead = _db.Employees.Where(e => e.Id == l.Employee.ManagerId && e.IsActive && e.Role == Role.TeamLead).Select(e => (int?)e.Id).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var byApprover = pending
            .SelectMany(p => p.Lead is { } lead ? [(Approver: lead, p.Hours)] : managers.Select(m => (Approver: m, p.Hours)))
            .GroupBy(p => p.Approver)
            .ToList();
        var alreadyApprovers = await _notifications.RecipientsNotifiedSinceAsync(NotificationType.TimeLogsAwaitingApproval, startOfDay, cancellationToken);
        var approverReminders = byApprover
            .Where(g => !alreadyApprovers.Contains(g.Key))
            .Select(g => NotificationTemplates.TimeLogsAwaitingApproval(g.Key, g.Count(), g.Sum(x => x.Hours)))
            .ToList();

        var drafts = await _db.Deductions.AsNoTracking()
            .Where(d => d.Status == DeductionStatus.Draft && d.CreatedAtUtc <= now - DraftAge)
            .Select(d => d.Amount)
            .ToListAsync(cancellationToken);
        var alreadyManagers = await _notifications.RecipientsNotifiedSinceAsync(NotificationType.DraftDeductionsPending, startOfDay, cancellationToken);
        var managerReminders = drafts.Count == 0
            ? []
            : managers.Where(m => !alreadyManagers.Contains(m)).Select(m => NotificationTemplates.DraftDeductionsPending(m, drafts.Count, drafts.Sum())).ToList();

        await _notifications.PublishAsync([.. approverReminders, .. managerReminders], cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return new Result(approverReminders.Count, managerReminders.Count);
    }
}
