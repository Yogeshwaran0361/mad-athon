using FluentValidation;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.ProfitShare;

/// <summary>What an award shows. It carries no revenue, profit or pay rates, so it is safe to show its owner.</summary>
public sealed record IncentiveAwardDto(
    int Id,
    int ChangeRequestId,
    string ChangeRequestTitle,
    int EmployeeId,
    string EmployeeName,
    decimal Hours,
    decimal SharePercent,
    decimal Amount,
    IncentiveAwardStatus Status,
    string ApplyMonth,
    int? DecidedByEmployeeId,
    string? RejectionReason,
    int? PayrollRunId,
    int Version);

/// <summary>The settlement of a closed change request. Manager, Finance and Admin only.</summary>
public sealed record ProfitShareSummaryDto(
    int ChangeRequestId,
    decimal ReceivedAmountExTax,
    decimal? LabourCostExTax,
    decimal? ProfitExTax,
    decimal? Pool,
    DateTime? SettledAtUtc,
    IReadOnlyList<IncentiveAwardDto> Awards);

/// <summary>An earning line on a payslip or preview: the profit share of one change request.</summary>
public sealed record EarningLineDto(int AwardId, int ChangeRequestId, string ChangeRequestTitle, decimal Hours, decimal SharePercent, decimal Amount);

public sealed record RejectAwardRequest(string Reason);

internal sealed class RejectAwardRequestValidator : AbstractValidator<RejectAwardRequest>
{
    public RejectAwardRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
