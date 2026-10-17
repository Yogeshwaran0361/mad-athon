using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Payslips;

/// <summary>An employee's statement for a locked payroll run. Its id is the run's employee row id.</summary>
public sealed record PayslipSummaryDto(
    long Id,
    int EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    PayMode PayMode,
    string PeriodMonth,
    int PayrollRunId,
    int RunVersion,
    decimal SalaryBase,
    decimal CapAmount,
    decimal Applied,
    decimal Refunds,
    decimal CarriedForward,
    decimal SettledManually,
    decimal NetAdjustment,
    DateTime PublishedAtUtc,
    decimal Earnings = 0m,
    decimal NetPay = 0m);

/// <summary>
/// Gross (salary base), + earning lines (change-request profit shares), − deduction items, = net. Earnings and
/// deductions are separate lines and never netted.
/// </summary>
public sealed record PayslipDetailDto(
    PayslipSummaryDto Payslip,
    PayrollRunEmployeeDto Employee,
    IReadOnlyList<PayrollRunItemDto> Items,
    IReadOnlyList<ReworkGuard.Application.ProfitShare.EarningLineDto>? EarningLines = null);

/// <summary>Everything the PDF shows, already resolved, so the renderer does no data access.</summary>
public sealed record PayslipDocument(PayslipDetailDto Detail, string SalaryBasis, decimal CapPercent, DateTime GeneratedAtUtc);
