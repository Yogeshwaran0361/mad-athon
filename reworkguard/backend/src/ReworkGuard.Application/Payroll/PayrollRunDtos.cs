using FluentValidation;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Payroll;

public sealed record PayrollRunSummaryDto(
    int Id,
    string PeriodMonth,
    int RunVersion,
    PayrollRunStatus Status,
    string InputsHash,
    decimal CapPercent,
    int EmployeeCount,
    decimal TotalApplied,
    decimal TotalCarriedForward,
    decimal TotalRefunds,
    decimal TotalSettledManually,
    int GeneratedByEmployeeId,
    DateTime GeneratedAtUtc,
    int? ReviewedByEmployeeId,
    DateTime? ReviewedAtUtc,
    int? ApprovedByEmployeeId,
    DateTime? ApprovedAtUtc,
    int? FinalizedByEmployeeId,
    DateTime? FinalizedAtUtc,
    int? LockedByEmployeeId,
    DateTime? LockedAtUtc,
    DateTime? SupersededAtUtc,
    int Version);

public sealed record PayrollRunEmployeeDto(
    long Id,
    int EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    PayMode PayMode,
    string SalaryBasis,
    bool ManualSettlement,
    decimal SalaryBase,
    decimal CapAmount,
    decimal CarriedIn,
    decimal CurrentDue,
    decimal Refunds,
    decimal CancelledByCompensation,
    decimal Applied,
    decimal CarriedForward,
    decimal SettledManually,
    decimal NetAdjustment,
    decimal Earnings,
    decimal NetPay);

public sealed record PayrollRunItemDto(
    int DeductionId,
    PayrollItemKind Kind,
    string OriginalApplyMonth,
    DateOnly WorkDate,
    int BugId,
    int TaskId,
    string TaskTitle,
    decimal Due,
    decimal Applied,
    decimal CarriedForward,
    decimal SettledManually,
    decimal CancelledByCompensation);

public sealed record ReconciliationCheckDto(string Name, bool Passed, string Detail);

public sealed record PayrollRunDetailDto(
    PayrollRunSummaryDto Run,
    IReadOnlyList<PayrollRunEmployeeDto> Employees,
    IReadOnlyList<ReconciliationCheckDto>? Reconciliation);

public sealed record PayrollRunEmployeeDetailDto(PayrollRunEmployeeDto Employee, IReadOnlyList<PayrollRunItemDto> Items);

public sealed record PayrollPeriodDto(string Month, bool IsLocked, int? FinalizedRunId, DateTime? LockedAtUtc);

public sealed record GeneratePayrollRunRequest(string PeriodMonth);

/// <summary>Every step names the run version the caller is looking at, so a stale screen cannot approve a different run.</summary>
public sealed record PayrollRunStepRequest(int ExpectedVersion);

internal sealed class GeneratePayrollRunRequestValidator : AbstractValidator<GeneratePayrollRunRequest>
{
    public GeneratePayrollRunRequestValidator()
    {
        RuleFor(r => r.PeriodMonth).Must(m => YearMonth.TryParse(m, out _)).WithMessage("periodMonth must be a month in yyyy-MM format.");
    }
}

internal sealed class PayrollRunStepRequestValidator : AbstractValidator<PayrollRunStepRequest>
{
    public PayrollRunStepRequestValidator()
    {
        RuleFor(r => r.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}
