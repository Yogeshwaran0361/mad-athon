namespace ReworkGuard.Domain.Enums;

/// <summary>Preview → Reviewed (Finance) → Approved (Manager) → Finalized (Finance) → Locked (Finance). Superseded when regenerated.</summary>
public enum PayrollRunStatus
{
    Preview,
    Reviewed,
    Approved,
    Finalized,
    Locked,
    Superseded,
}
