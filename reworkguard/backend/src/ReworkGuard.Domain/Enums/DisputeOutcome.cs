namespace ReworkGuard.Domain.Enums;

public enum DisputeOutcome
{
    /// <summary>The employee was right: the line is cancelled, or replaced by a smaller one.</summary>
    Upheld,

    /// <summary>The deduction stands as notified.</summary>
    Rejected,
}
