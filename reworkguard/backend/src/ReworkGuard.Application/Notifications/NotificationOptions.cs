namespace ReworkGuard.Application.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Public address of the web app, used to turn notification links into absolute URLs in emails.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>Messages delivered per outbox run.</summary>
    public int OutboxBatchSize { get; set; } = 50;
}
