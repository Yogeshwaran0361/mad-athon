using System.Globalization;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Notifications;

/// <summary>A notification to be written in the caller's unit of work.</summary>
public sealed record NotificationRequest(
    int RecipientEmployeeId,
    NotificationType Type,
    string Title,
    string Body,
    string? Link,
    string? EntityType,
    string? EntityId);

/// <summary>The wording of every notification, in one place.</summary>
public static class NotificationTemplates
{
    /// <summary>Indian digit grouping (1,23,456.50) without relying on ICU culture data, which containers often lack.</summary>
    public static string Money(decimal amount)
    {
        var text = Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture);
        var whole = text[..^3];
        var grouped = whole.Length <= 3 ? whole : string.Join(',', SplitPairs(whole[..^3]).Append(whole[^3..]));
        return (amount < 0 ? "-INR " : "INR ") + grouped + text[^3..];
    }

    private static IEnumerable<string> SplitPairs(string digits)
    {
        var first = digits.Length % 2;
        if (first == 1)
        {
            yield return digits[..1];
        }

        for (var i = first; i < digits.Length; i += 2)
        {
            yield return digits.Substring(i, 2);
        }
    }

    public static string IstTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), WorkTimeZone.Zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST";

    public static NotificationRequest DeductionsNotified(int employeeId, int lines, decimal total, DateTime windowEndsAtUtc, int windowDays) => new(
        employeeId,
        NotificationType.DeductionNotified,
        $"Deduction notice: {Money(total)}",
        $"{Lines(lines)} totalling {Money(total)} {(lines == 1 ? "was" : "were")} raised for corrective work on defects attributed to you. "
        + $"You have {windowDays} working day(s) to review and dispute: the window closes {IstTime(windowEndsAtUtc)}.",
        "/my-deductions",
        "Employee",
        Id(employeeId));

    public static NotificationRequest DisputeWindowClosing(int employeeId, int lines, decimal total, DateTime windowEndsAtUtc) => new(
        employeeId,
        NotificationType.DisputeWindowClosing,
        "Dispute window closing soon",
        $"The dispute window for {Lines(lines).ToLowerInvariant()} totalling {Money(total)} closes {IstTime(windowEndsAtUtc)}. After that the lines go to final approval.",
        "/my-deductions",
        "Employee",
        Id(employeeId));

    public static NotificationRequest DisputeRaised(int managerId, int deductionId, string employeeName, decimal amount) => new(
        managerId,
        NotificationType.DisputeRaised,
        $"Dispute raised by {employeeName}",
        $"{employeeName} disputed deduction line {deductionId} ({Money(amount)}). It needs a decision before it can be approved.",
        $"/deductions/{deductionId}",
        "Deduction",
        Id(deductionId));

    public static NotificationRequest DisputeResolved(int employeeId, int deductionId, DisputeOutcome outcome, decimal originalAmount, decimal? newAmount, string justification) => new(
        employeeId,
        NotificationType.DisputeResolved,
        outcome == DisputeOutcome.Upheld ? "Your dispute was upheld" : "Your dispute was rejected",
        (outcome, newAmount) switch
        {
            (DisputeOutcome.Upheld, null) => $"Deduction line {deductionId} ({Money(originalAmount)}) was cancelled.",
            (DisputeOutcome.Upheld, _) => $"Deduction line {deductionId} was reduced from {Money(originalAmount)} to {Money(newAmount.Value)}.",
            _ => $"Deduction line {deductionId} ({Money(originalAmount)}) stands.",
        } + $" Reason: {Shorten(justification, 1500)}",
        $"/deductions/{deductionId}",
        "Deduction",
        Id(deductionId));

    public static NotificationRequest DeductionsApproved(int employeeId, int lines, decimal total) => new(
        employeeId,
        NotificationType.DeductionApproved,
        $"Deduction approved: {Money(total)}",
        $"{Lines(lines)} totalling {Money(total)} {(lines == 1 ? "was" : "were")} approved and will be included in a coming payroll run.",
        "/my-deductions",
        "Employee",
        Id(employeeId));

    public static NotificationRequest DeductionCancelled(int employeeId, int deductionId, decimal amount, string reason) => new(
        employeeId,
        NotificationType.DeductionCancelled,
        "Deduction cancelled",
        $"Deduction line {deductionId} ({Money(amount)}) was cancelled. Reason: {Shorten(reason, 1500)}",
        $"/deductions/{deductionId}",
        "Deduction",
        Id(deductionId));

    public static NotificationRequest CompensationRaised(int employeeId, int deductionId, decimal amount, string applyMonth) => new(
        employeeId,
        NotificationType.CompensationRaised,
        $"Refund of {Money(-amount)} scheduled",
        $"An applied deduction was corrected. {Money(-amount)} will be returned in the {applyMonth} payroll.",
        $"/deductions/{deductionId}",
        "Deduction",
        Id(deductionId));

    public static NotificationRequest BugReported(int delivererId, int bugId, int taskId, string taskTitle, bool withinWarranty) => new(
        delivererId,
        NotificationType.BugReported,
        $"Bug reported on {Shorten(taskTitle, 150)}",
        $"QA reported bug {bugId} on a task you delivered. The task is reopened"
        + (withinWarranty ? " and the bug is inside the warranty window." : "; the bug is outside the warranty window."),
        $"/tasks/{taskId}",
        "Bug",
        Id(bugId));

    public static NotificationRequest TimeLogRejected(int employeeId, int timeLogId, int taskId, DateOnly workDate, decimal hours, string? reason) => new(
        employeeId,
        NotificationType.TimeLogRejected,
        "Time log rejected",
        $"Your {hours.ToString("0.##", CultureInfo.InvariantCulture)} h logged for {workDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} was rejected."
        + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Reason: {Shorten(reason, 1500)}"),
        $"/tasks/{taskId}",
        "TimeLog",
        Id(timeLogId));

    public static NotificationRequest DeductionsReadyForApproval(int managerId, int lines, decimal total) => new(
        managerId,
        NotificationType.DeductionsReadyForApproval,
        $"{Lines(lines)} ready for approval",
        $"{Lines(lines)} totalling {Money(total)} passed {(lines == 1 ? "its" : "their")} dispute window without a dispute and {(lines == 1 ? "is" : "are")} waiting for final approval.",
        "/deductions?status=Notified",
        null,
        null);

    public static NotificationRequest DisputeAwaitingDecision(int managerId, int disputes, DateTime oldestRaisedAtUtc) => new(
        managerId,
        NotificationType.DisputeAwaitingDecision,
        disputes == 1 ? "A dispute is waiting for your decision" : $"{disputes} disputes are waiting for a decision",
        $"{(disputes == 1 ? "One dispute has" : $"{disputes} disputes have")} been open for more than two working days; the oldest was raised {IstTime(oldestRaisedAtUtc)}. "
        + "Disputed lines cannot be paid or cancelled until they are resolved.",
        "/deductions?openDispute=true",
        null,
        null);

    public static NotificationRequest TimeLogsAwaitingApproval(int approverId, int logs, decimal hours) => new(
        approverId,
        NotificationType.TimeLogsAwaitingApproval,
        $"{logs} time log{(logs == 1 ? string.Empty : "s")} waiting for approval",
        $"{logs} time log{(logs == 1 ? string.Empty : "s")} ({hours.ToString("0.##", CultureInfo.InvariantCulture)} h) from your team {(logs == 1 ? "has" : "have")} been waiting more than a day for approval.",
        "/approvals",
        null,
        null);

    public static NotificationRequest DraftDeductionsPending(int managerId, int lines, decimal total) => new(
        managerId,
        NotificationType.DraftDeductionsPending,
        $"{Lines(lines)} not yet sent to employees",
        $"{Lines(lines)} totalling {Money(total)} {(lines == 1 ? "has" : "have")} been in Draft for more than two days. Employees can only dispute once they are notified.",
        "/deductions?status=Draft",
        null,
        null);

    private static string Lines(int count) => count == 1 ? "One deduction line" : $"{count} deduction lines";

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
