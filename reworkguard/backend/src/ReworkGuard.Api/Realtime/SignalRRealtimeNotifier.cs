using Microsoft.AspNetCore.SignalR;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Notifications;

namespace ReworkGuard.Api.Realtime;

internal sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<NotificationsHub> hub)
    {
        _hub = hub;
    }

    public Task PushAsync(int recipientEmployeeId, NotificationDto notification, CancellationToken cancellationToken) =>
        _hub.Clients.Group(NotificationsHub.GroupFor(recipientEmployeeId)).SendAsync(NotificationsHub.NotificationReceived, notification, cancellationToken);
}
