using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ReworkGuard.Infrastructure.Auth;

namespace ReworkGuard.Api.Realtime;

/// <summary>
/// Server-to-client only: each signed-in connection joins its own user group and receives
/// <see cref="NotificationReceived"/> events. Clients cannot send anything through the hub.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string NotificationReceived = "notificationReceived";

    public static string GroupFor(int employeeId) => "employee:" + employeeId.ToString(CultureInfo.InvariantCulture);

    public override async Task OnConnectedAsync()
    {
        var subject = Context.User?.FindFirst(ClaimNames.Subject)?.Value;
        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var employeeId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(employeeId));
        await base.OnConnectedAsync();
    }
}
