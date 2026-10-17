namespace ReworkGuard.Domain.Enums;

/// <summary>What caused a task status change. Some transitions are only legal for a specific trigger.</summary>
public enum TransitionTrigger
{
    Manual,
    BugReported,
    CorrectiveWorkStarted,
    WarrantyExpired,
}
