using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Payroll;

internal static class PayrollTemplates
{
    public static NotificationRequest AwaitingApproval(int managerId, PayrollRun run) => new(
        managerId,
        NotificationType.PayrollRunAwaitingApproval,
        $"Payroll {run.PeriodMonth} (version {run.RunVersion}) needs your approval",
        $"Finance reviewed the {run.PeriodMonth} payroll run: {run.EmployeeCount} employees, {NotificationTemplates.Money(run.TotalApplied)} to deduct, "
        + $"{NotificationTemplates.Money(-run.TotalRefunds)} to refund, {NotificationTemplates.Money(run.TotalCarriedForward)} carried forward.",
        $"/payroll/runs/{run.Id}",
        "PayrollRun",
        run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static NotificationRequest Finalized(int recipientId, PayrollRun run) => new(
        recipientId,
        NotificationType.PayrollRunFinalized,
        $"Payroll {run.PeriodMonth} finalized",
        $"Version {run.RunVersion} of the {run.PeriodMonth} payroll passed reconciliation and was finalized. The month is now closed to new deductions.",
        $"/payroll/runs/{run.Id}",
        "PayrollRun",
        run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static NotificationRequest PayslipPublished(PayrollRunEmployee row, PayrollRun run) => new(
        row.EmployeeId,
        NotificationType.PayslipPublished,
        $"Your {run.PeriodMonth} payslip is ready",
        row.NetAdjustment switch
        {
            < 0 => $"{NotificationTemplates.Money(-row.NetAdjustment)} was deducted for rework in {run.PeriodMonth}.",
            > 0 => $"{NotificationTemplates.Money(row.NetAdjustment)} is refunded to you in {run.PeriodMonth}.",
            _ => $"No rework adjustment was applied in {run.PeriodMonth}.",
        }
        + (row.CarriedForward > 0 ? $" {NotificationTemplates.Money(row.CarriedForward)} carries forward to next month under the cap." : string.Empty)
        + (row.SettledManually > 0 ? $" {NotificationTemplates.Money(row.SettledManually)} is left for HR to settle." : string.Empty),
        $"/payslips/{row.Id}",
        "Payslip",
        row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
