using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Jobs;

/// <summary>
/// Runs every few minutes and is safe to repeat:
/// reminds employees a day before their dispute window closes; when a window closes without a dispute, records it
/// and tells the managers the lines are ready for final approval; and reminds managers of disputes left open for
/// more than <see cref="DisputeReminderWorkingDays"/> working days (at most once a day per dispute).
/// </summary>
public sealed class DeductionWindowJob
{
    public const int DisputeReminderWorkingDays = 2;
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IWorkCalendar _calendar;
    private readonly NotificationPublisher _notifications;

    public DeductionWindowJob(IApplicationDbContext db, IClock clock, IAuditWriter audit, IWorkCalendar calendar, NotificationPublisher notifications)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _calendar = calendar;
        _notifications = notifications;
    }

    public sealed record Result(int RemindedLines, int ExpiredLines, int DisputeReminders);

    public async Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        var reminded = await RemindClosingWindowsAsync(cancellationToken);
        var expired = await RecordExpiredWindowsAsync(cancellationToken);
        var disputes = await RemindOpenDisputesAsync(cancellationToken);
        return new Result(reminded, expired, disputes);
    }

    private async Task<int> RemindClosingWindowsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var horizon = now + ReminderLeadTime;
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var lines = await _db.Deductions
            .Where(d => d.Status == DeductionStatus.Notified && d.WindowReminderSentAtUtc == null
                        && d.DisputeWindowEndsAtUtc >= now && d.DisputeWindowEndsAtUtc <= horizon)
            .ToListAsync(cancellationToken);
        if (lines.Count == 0)
        {
            return 0;
        }

        foreach (var line in lines)
        {
            line.MarkWindowReminderSent(now);
        }

        await _notifications.PublishAsync(
            lines.GroupBy(l => l.EmployeeId)
                .Select(g => NotificationTemplates.DisputeWindowClosing(g.Key, g.Count(), g.Sum(l => l.Amount), g.Min(l => l.DisputeWindowEndsAtUtc!.Value)))
                .ToList(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return lines.Count;
    }

    private async Task<int> RecordExpiredWindowsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var lines = await _db.Deductions
            .Where(d => d.Status == DeductionStatus.Notified && d.WindowExpiryRecordedAtUtc == null && d.DisputeWindowEndsAtUtc < now)
            .ToListAsync(cancellationToken);
        if (lines.Count == 0)
        {
            return 0;
        }

        foreach (var line in lines)
        {
            line.RecordWindowExpired(now);
            _audit.Record(AuditEntities.Deduction, line.Id.ToString(CultureInfo.InvariantCulture), "DisputeWindowExpired", new { line.EmployeeId, line.Amount, line.DisputeWindowEndsAtUtc });
        }

        var managers = await _notifications.ActiveEmployeesInRoleAsync(Role.Manager, cancellationToken);
        var total = lines.Sum(l => l.Amount);
        await _notifications.PublishAsync(managers.Select(m => NotificationTemplates.DeductionsReadyForApproval(m, lines.Count, total)).ToList(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return lines.Count;
    }

    private async Task<int> RemindOpenDisputesAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var oncePerDay = now.AddDays(-1);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var open = await _db.DeductionDisputes
            .Where(d => d.Outcome == null && (d.ReminderSentAtUtc == null || d.ReminderSentAtUtc <= oncePerDay))
            .ToListAsync(cancellationToken);

        var overdue = new List<Domain.Entities.DeductionDispute>();
        foreach (var dispute in open)
        {
            var dueAt = await _calendar.WindowEndUtcAsync(dispute.RaisedAtUtc, DisputeReminderWorkingDays, cancellationToken);
            if (dueAt < now)
            {
                overdue.Add(dispute);
            }
        }

        if (overdue.Count == 0)
        {
            return 0;
        }

        foreach (var dispute in overdue)
        {
            dispute.MarkReminderSent(now);
        }

        var managers = await _notifications.ActiveEmployeesInRoleAsync(Role.Manager, cancellationToken);
        var oldest = overdue.Min(d => d.RaisedAtUtc);
        await _notifications.PublishAsync(managers.Select(m => NotificationTemplates.DisputeAwaitingDecision(m, overdue.Count, oldest)).ToList(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return overdue.Count;
    }

    /// <summary>Start of today in IST, as UTC.</summary>
    internal static DateTime StartOfTodayUtc(DateTime utcNow) => WorkTimeZone.StartOfDayUtc(WorkTimeZone.ToWorkDate(utcNow));
}
