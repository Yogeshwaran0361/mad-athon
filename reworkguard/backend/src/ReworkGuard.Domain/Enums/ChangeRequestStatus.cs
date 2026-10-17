namespace ReworkGuard.Domain.Enums;

/// <summary>Requested → Approved → InProgress → Completed → Closed, or Requested → Rejected.</summary>
public enum ChangeRequestStatus
{
    Requested,
    Approved,
    InProgress,
    Completed,
    Closed,
    Rejected,
}
