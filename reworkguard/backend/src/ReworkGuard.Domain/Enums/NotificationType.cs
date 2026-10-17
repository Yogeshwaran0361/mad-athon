namespace ReworkGuard.Domain.Enums;

/// <summary>What a notification is about. Each type has its own email preference.</summary>
public enum NotificationType
{
    DeductionNotified,
    DisputeWindowClosing,
    DisputeRaised,
    DisputeResolved,
    DeductionApproved,
    DeductionCancelled,
    CompensationRaised,
    BugReported,
    TimeLogRejected,
    PayrollRunAwaitingApproval,
    PayrollRunFinalized,
    PayslipPublished,
    DeductionsReadyForApproval,
    DisputeAwaitingDecision,
    TimeLogsAwaitingApproval,
    DraftDeductionsPending,
}
