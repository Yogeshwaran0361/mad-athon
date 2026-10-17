using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Notifications;

/// <summary>
/// Writes notifications and their outbox messages into the caller's unit of work, so they commit (or roll back)
/// with the change they describe. Nothing is sent here: the outbox job delivers after commit.
/// Every notification gets an in-app delivery; email follows the recipient's preference. Inactive employees are not notified.
/// </summary>
public sealed class NotificationPublisher
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public NotificationPublisher(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task PublishAsync(IReadOnlyCollection<NotificationRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return;
        }

        var recipientIds = requests.Select(r => r.RecipientEmployeeId).Distinct().ToList();
        var active = (await _db.Employees.AsNoTracking()
                .Where(e => recipientIds.Contains(e.Id) && e.IsActive)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var emailOff = (await _db.NotificationPreferences.AsNoTracking()
                .Where(p => recipientIds.Contains(p.EmployeeId) && !p.EmailEnabled)
                .Select(p => new { p.EmployeeId, p.Type })
                .ToListAsync(cancellationToken))
            .Select(p => (p.EmployeeId, p.Type))
            .ToHashSet();

        var now = _clock.UtcNow;
        foreach (var request in requests.Where(r => active.Contains(r.RecipientEmployeeId)))
        {
            var notification = new Notification(
                request.RecipientEmployeeId, request.Type, request.Title, request.Body, request.Link, request.EntityType, request.EntityId, now);
            _db.Notifications.Add(notification);
            _db.OutboxMessages.Add(new OutboxMessage(notification, NotificationChannel.InApp, now));
            if (!emailOff.Contains((request.RecipientEmployeeId, request.Type)))
            {
                _db.OutboxMessages.Add(new OutboxMessage(notification, NotificationChannel.Email, now));
            }
        }
    }

    public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken) => PublishAsync([request], cancellationToken);

    /// <summary>Recipients who already received a <paramref name="type"/> notification at or after <paramref name="sinceUtc"/>; lets daily reminders run more than once a day without repeating.</summary>
    public async Task<HashSet<int>> RecipientsNotifiedSinceAsync(NotificationType type, DateTime sinceUtc, CancellationToken cancellationToken) =>
        (await _db.Notifications.AsNoTracking()
            .Where(n => n.Type == type && n.CreatedAtUtc >= sinceUtc)
            .Select(n => n.RecipientEmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken))
        .ToHashSet();

    /// <summary>Active employees holding <paramref name="role"/>, e.g. the managers who decide disputes.</summary>
    public async Task<IReadOnlyList<int>> ActiveEmployeesInRoleAsync(Role role, CancellationToken cancellationToken) =>
        await _db.Employees.AsNoTracking().Where(e => e.Role == role && e.IsActive).Select(e => e.Id).ToListAsync(cancellationToken);
}
