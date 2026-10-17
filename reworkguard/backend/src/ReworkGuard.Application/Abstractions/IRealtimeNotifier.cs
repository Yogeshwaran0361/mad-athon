using ReworkGuard.Application.Notifications;

namespace ReworkGuard.Application.Abstractions;

/// <summary>Pushes an in-app notification to the recipient's open browser sessions, if any.</summary>
public interface IRealtimeNotifier
{
    Task PushAsync(int recipientEmployeeId, NotificationDto notification, CancellationToken cancellationToken);
}
