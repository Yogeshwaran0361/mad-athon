namespace ReworkGuard.Domain.Enums;

/// <summary>Delivery state of one outbox message.</summary>
public enum OutboxStatus
{
    Pending,
    Sent,
    Skipped,
    DeadLettered,
}
