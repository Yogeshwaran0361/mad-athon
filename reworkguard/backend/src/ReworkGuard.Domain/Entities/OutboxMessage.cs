using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// One delivery of a notification on one channel, written in the same transaction as the business change and
/// delivered afterwards by a background job. Delivery is at least once: a claimed message whose worker dies is
/// picked up again when its lease runs out. Failures back off exponentially and dead-letter after
/// <see cref="MaxAttempts"/> attempts.
/// </summary>
public class OutboxMessage : IVersioned
{
    public const int MaxAttempts = 5;
    public const int MaxErrorLength = 1000;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);

    private OutboxMessage()
    {
    }

    public OutboxMessage(Notification notification, NotificationChannel channel, DateTime createdAtUtc)
    {
        Notification = notification;
        Channel = channel;
        Status = OutboxStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    public long NotificationId { get; private set; }

    public Notification Notification { get; private set; } = null!;

    public NotificationChannel Channel { get; private set; }

    public OutboxStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime NextAttemptAtUtc { get; private set; }

    public DateTime? ProcessedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public int Version { get; set; }

    /// <summary>Takes the message for one delivery attempt; nobody else picks it up until the lease ends.</summary>
    public void Claim(DateTime utcNow)
    {
        EnsurePending();
        Attempts++;
        NextAttemptAtUtc = utcNow + Lease;
    }

    public void MarkSent(DateTime utcNow)
    {
        EnsurePending();
        Status = OutboxStatus.Sent;
        ProcessedAtUtc = utcNow;
        LastError = null;
    }

    /// <summary>Nothing to deliver (e.g. the recipient has left). Final, not an error.</summary>
    public void Skip(string reason, DateTime utcNow)
    {
        EnsurePending();
        Status = OutboxStatus.Skipped;
        ProcessedAtUtc = utcNow;
        LastError = Truncate(reason);
    }

    /// <summary>Records a failed attempt: retry after 1, 2, 4, 8 minutes, then dead-letter.</summary>
    public void MarkFailed(string error, DateTime utcNow)
    {
        EnsurePending();
        LastError = Truncate(error);
        if (Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.DeadLettered;
            ProcessedAtUtc = utcNow;
            return;
        }

        NextAttemptAtUtc = utcNow + (FirstRetryDelay * Math.Pow(2, Math.Max(0, Attempts - 1)));
    }

    /// <summary>An administrator sends a dead-lettered message round again with a fresh set of attempts.</summary>
    public void Requeue(DateTime utcNow)
    {
        if (Status != OutboxStatus.DeadLettered)
        {
            throw new DomainException("outbox.not_dead_lettered", "Only dead-lettered messages can be retried.");
        }

        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAtUtc = utcNow;
        ProcessedAtUtc = null;
    }

    private void EnsurePending()
    {
        if (Status != OutboxStatus.Pending)
        {
            throw new DomainException("outbox.not_pending", $"Outbox message {Id} is already {Status}.");
        }
    }

    private static string Truncate(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
}
