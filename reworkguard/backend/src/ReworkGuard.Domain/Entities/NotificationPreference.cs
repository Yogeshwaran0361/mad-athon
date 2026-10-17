using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Notifications;

namespace ReworkGuard.Domain.Entities;

/// <summary>An employee's email choice for one notification type. No row means email is on.</summary>
public class NotificationPreference
{
    private NotificationPreference()
    {
    }

    public NotificationPreference(int employeeId, NotificationType type, bool emailEnabled)
    {
        EmployeeId = employeeId;
        Type = type;
        SetEmail(emailEnabled);
    }

    public int EmployeeId { get; private set; }

    public NotificationType Type { get; private set; }

    public bool EmailEnabled { get; private set; }

    public void SetEmail(bool enabled)
    {
        if (!enabled && NotificationCatalog.IsEmailMandatory(Type))
        {
            throw new DomainException("notification.email_mandatory", $"Email for {Type} cannot be turned off: it is a formal notice.");
        }

        EmailEnabled = enabled;
    }
}
