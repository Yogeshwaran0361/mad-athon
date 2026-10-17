using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// Append-only audit trail entry. Never updated or deleted. The database assigns <see cref="Sequence"/>,
/// <see cref="PrevHash"/> and <see cref="Hash"/> on insert, chaining every entry to the one before it, so any later
/// edit, deletion or reordering is detectable.
/// </summary>
public class AuditEvent : IAppendOnly
{
    private AuditEvent()
    {
    }

    public AuditEvent(DateTime occurredAtUtc, int? actorEmployeeId, string entityType, string entityId, string action, string detailsJson)
    {
        OccurredAtUtc = occurredAtUtc;
        ActorEmployeeId = actorEmployeeId;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        DetailsJson = detailsJson;
    }

    public long Id { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public int? ActorEmployeeId { get; private set; }

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string DetailsJson { get; private set; } = "{}";

    public long Sequence { get; private set; }

    public string PrevHash { get; private set; } = string.Empty;

    public string Hash { get; private set; } = string.Empty;
}
