using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Notifications;

namespace ReworkGuard.Application.Notifications;

/// <summary>The caller's own inbox and email preferences, plus the administrator's view of the delivery outbox.</summary>
public sealed class NotificationService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<UpdateNotificationPreferencesRequest> _preferencesValidator;

    public NotificationService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IValidator<UpdateNotificationPreferencesRequest> preferencesValidator)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _preferencesValidator = preferencesValidator;
    }

    public Task<PagedResult<NotificationDto>> ListMineAsync(bool unreadOnly, PageRequest page, CancellationToken cancellationToken)
    {
        var mine = _db.Notifications.AsNoTracking().Where(n => n.RecipientEmployeeId == _user.EmployeeId);
        if (unreadOnly)
        {
            mine = mine.Where(n => n.ReadAtUtc == null);
        }

        return Project(mine.OrderByDescending(n => n.Id)).ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<UnreadCountDto> UnreadCountAsync(CancellationToken cancellationToken) =>
        new(await _db.Notifications.CountAsync(n => n.RecipientEmployeeId == _user.EmployeeId && n.ReadAtUtc == null, cancellationToken));

    /// <summary>Marks one of the caller's notifications read. Someone else's notification is reported as not found.</summary>
    public async Task<NotificationDto> MarkReadAsync(long id, CancellationToken cancellationToken)
    {
        var notification = await _db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.RecipientEmployeeId == _user.EmployeeId, cancellationToken)
            ?? throw new NotFoundException("Notification", id);
        notification.MarkRead(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    public async Task<UnreadCountDto> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var unread = await _db.Notifications.Where(n => n.RecipientEmployeeId == _user.EmployeeId && n.ReadAtUtc == null).ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead(now);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new UnreadCountDto(0);
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        var stored = await _db.NotificationPreferences.AsNoTracking()
            .Where(p => p.EmployeeId == _user.EmployeeId)
            .ToDictionaryAsync(p => p.Type, p => p.EmailEnabled, cancellationToken);
        return NotificationCatalog.All
            .Select(t => new NotificationPreferenceDto(t, stored.GetValueOrDefault(t, true), InAppEnabled: true, NotificationCatalog.IsEmailMandatory(t)))
            .ToList();
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> UpdatePreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        await _preferencesValidator.ValidateAndThrowAsync(request, cancellationToken);
        var stored = await _db.NotificationPreferences.Where(p => p.EmployeeId == _user.EmployeeId).ToDictionaryAsync(p => p.Type, cancellationToken);
        foreach (var change in request.Preferences)
        {
            if (stored.TryGetValue(change.Type, out var existing))
            {
                existing.SetEmail(change.EmailEnabled);
            }
            else
            {
                _db.NotificationPreferences.Add(new NotificationPreference(_user.EmployeeId, change.Type, change.EmailEnabled));
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(cancellationToken);
    }

    public Task<PagedResult<OutboxMessageDto>> ListOutboxAsync(OutboxStatus? status, PageRequest page, CancellationToken cancellationToken)
    {
        var messages = _db.OutboxMessages.AsNoTracking();
        if (status is not null)
        {
            messages = messages.Where(m => m.Status == status);
        }

        return messages
            .OrderByDescending(m => m.Id)
            .Select(m => new OutboxMessageDto(
                m.Id,
                m.NotificationId,
                m.Channel,
                m.Status,
                m.Attempts,
                m.Notification.RecipientEmployeeId,
                m.Notification.Type,
                m.CreatedAtUtc,
                m.NextAttemptAtUtc,
                m.ProcessedAtUtc,
                m.LastError))
            .ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Sends a dead-lettered message round again (e.g. after the mail provider outage is fixed).</summary>
    public async Task RetryOutboxAsync(long id, CancellationToken cancellationToken)
    {
        var message = await _db.OutboxMessages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new NotFoundException("Outbox message", id);
        message.Requeue(_clock.UtcNow);
        _audit.Record(AuditEntities.Notification, id.ToString(System.Globalization.CultureInfo.InvariantCulture), "OutboxRetried", new { message.NotificationId, message.Channel, message.LastError });
        await _db.SaveChangesAsync(cancellationToken);
    }

    internal static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.Title, n.Body, n.Link, n.EntityType, n.EntityId, n.CreatedAtUtc, n.ReadAtUtc);

    private static IQueryable<NotificationDto> Project(IQueryable<Notification> notifications) =>
        notifications.Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.EntityType, n.EntityId, n.CreatedAtUtc, n.ReadAtUtc));
}
