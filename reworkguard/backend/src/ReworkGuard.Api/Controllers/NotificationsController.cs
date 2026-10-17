using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

/// <summary>The caller's own notification inbox and email preferences. Live updates arrive over the SignalR hub at /hubs/notifications.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/notifications")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications)
    {
        _notifications = notifications;
    }

    /// <summary>The caller's notifications, newest first.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Everyone)]
    public Task<PagedResult<NotificationDto>> List([FromQuery] bool unreadOnly, [FromQuery] PagingQuery paging, CancellationToken cancellationToken) =>
        _notifications.ListMineAsync(unreadOnly, paging.ToPageRequest(), cancellationToken);

    [HttpGet("unread-count")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<UnreadCountDto> UnreadCount(CancellationToken cancellationToken) => _notifications.UnreadCountAsync(cancellationToken);

    [HttpPost("{id:long}/read")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<NotificationDto> MarkRead(long id, CancellationToken cancellationToken) => _notifications.MarkReadAsync(id, cancellationToken);

    [HttpPost("read-all")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<UnreadCountDto> MarkAllRead(CancellationToken cancellationToken) => _notifications.MarkAllReadAsync(cancellationToken);

    /// <summary>Email on/off per notification type. In-app is always on; formal notices cannot turn email off.</summary>
    [HttpGet("preferences")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<IReadOnlyList<NotificationPreferenceDto>> Preferences(CancellationToken cancellationToken) =>
        _notifications.GetPreferencesAsync(cancellationToken);

    [HttpPut("preferences")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<NotificationPreferenceDto>> UpdatePreferences(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken) =>
        _notifications.UpdatePreferencesAsync(request, cancellationToken);
}

/// <summary>Delivery outbox for operations: inspect failures and retry dead-lettered messages.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/admin/outbox")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class OutboxAdminController : ControllerBase
{
    private readonly NotificationService _notifications;

    public OutboxAdminController(NotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    [Authorize(Roles = Roles.Administrators)]
    public Task<PagedResult<OutboxMessageDto>> List([FromQuery] OutboxStatus? status, [FromQuery] PagingQuery paging, CancellationToken cancellationToken) =>
        _notifications.ListOutboxAsync(status, paging.ToPageRequest(), cancellationToken);

    [HttpPost("{id:long}/retry")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken)
    {
        await _notifications.RetryOutboxAsync(id, cancellationToken);
        return NoContent();
    }
}
