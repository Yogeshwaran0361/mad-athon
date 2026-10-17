using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Notifications;

/// <summary>
/// Which notification types an employee may silence by email. Notices that carry a legal or payroll consequence
/// (a deduction they can dispute, the outcome of their dispute, a published payslip) always go by email as well.
/// In-app notifications are always kept: they are the employee's record of what they were told.
/// </summary>
public static class NotificationCatalog
{
    private static readonly HashSet<NotificationType> MandatoryEmail =
    [
        NotificationType.DeductionNotified,
        NotificationType.DisputeResolved,
        NotificationType.PayslipPublished,
    ];

    public static IReadOnlyList<NotificationType> All { get; } = Enum.GetValues<NotificationType>();

    public static bool IsEmailMandatory(NotificationType type) => MandatoryEmail.Contains(type);
}
