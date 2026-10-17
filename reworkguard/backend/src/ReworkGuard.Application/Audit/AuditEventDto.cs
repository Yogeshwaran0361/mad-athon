namespace ReworkGuard.Application.Audit;

public sealed record AuditEventDto(
    long Id,
    long Sequence,
    DateTime OccurredAtUtc,
    int? ActorEmployeeId,
    string ActorName,
    string EntityType,
    string EntityId,
    string Action,
    string DetailsJson,
    string Hash);

public sealed record AuditEventQuery(string? EntityType, string? EntityId, int? ActorEmployeeId, string? Action, DateTime? FromUtc, DateTime? ToUtc);

/// <summary>
/// Result of re-computing the audit hash chain. <see cref="FirstBrokenSequence"/> names the first entry whose hash,
/// link to the previous entry or sequence number does not check out; <see cref="Reason"/> says which.
/// </summary>
public sealed record AuditVerificationDto(
    bool Valid,
    long CheckedCount,
    long HeadSequence,
    string HeadHash,
    long? FirstBrokenSequence,
    long? FirstBrokenId,
    string? Reason,
    DateTime VerifiedAtUtc);
