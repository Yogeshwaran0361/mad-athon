using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>A message to one employee. Shown in the in-app inbox; delivery by email goes through the outbox.</summary>
public class Notification
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 2000;
    public const int MaxLinkLength = 300;

    private Notification()
    {
    }

    public Notification(int recipientEmployeeId, NotificationType type, string title, string body, string? link, string? entityType, string? entityId, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
        {
            throw new DomainException("notification.invalid_title", $"A notification title must be 1 to {MaxTitleLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength)
        {
            throw new DomainException("notification.invalid_body", $"A notification body must be 1 to {MaxBodyLength} characters.");
        }

        if (link is not null && (link.Length > MaxLinkLength || !link.StartsWith('/')))
        {
            throw new DomainException("notification.invalid_link", "A notification link must be an app-relative path.");
        }

        RecipientEmployeeId = recipientEmployeeId;
        Type = type;
        Title = title;
        Body = body;
        Link = link;
        EntityType = entityType;
        EntityId = entityId;
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    public int RecipientEmployeeId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string? Link { get; private set; }

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ReadAtUtc { get; private set; }

    public bool IsRead => ReadAtUtc is not null;

    /// <summary>Idempotent: reading an already-read notification keeps the first read time.</summary>
    public void MarkRead(DateTime utcNow) => ReadAtUtc ??= utcNow;
}
