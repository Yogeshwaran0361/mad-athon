using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Payroll;

public sealed record DeductionPreviewLineDto(
    int DeductionId,
    PayrollItemKind Kind,
    DeductionStatus Status,
    string ApplyMonth,
    DateOnly WorkDate,
    int BugId,
    int TaskId,
    string TaskTitle,
    string ModuleName,
    decimal Hours,
    decimal HourlyRate,
    decimal SharePercent,
    decimal Due,
    decimal Applied,
    decimal CarriedForward);

/// <summary>
/// What a pay month's payroll deducts for one employee. For a finalized month the figures come from the run; otherwise
/// they are a live estimate from the lines approved so far. Pending lists lines that are due by the month but not yet
/// approved, so they are not in the figures.
/// </summary>
public sealed record DeductionPreviewDto(
    int EmployeeId,
    string EmployeeName,
    PayMode PayMode,
    string PeriodMonth,
    string Source,
    int? PayrollRunId,
    decimal SalaryBase,
    string SalaryBasis,
    decimal CapPercent,
    decimal CapAmount,
    decimal CarriedIn,
    decimal CurrentDue,
    decimal Refunds,
    decimal Applied,
    decimal CarriedForward,
    decimal SettledManually,
    decimal NetAdjustment,
    IReadOnlyList<DeductionPreviewLineDto> Lines,
    IReadOnlyList<DeductionPreviewLineDto> Pending,
    decimal Earnings = 0m,
    decimal NetPay = 0m,
    IReadOnlyList<ReworkGuard.Application.ProfitShare.EarningLineDto>? EarningLines = null);
