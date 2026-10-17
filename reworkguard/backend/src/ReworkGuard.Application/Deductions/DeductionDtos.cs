using FluentValidation;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Deductions;

public sealed record DeductionDisputeDto(
    int Id,
    string Reason,
    DateTime RaisedAtUtc,
    DisputeOutcome? Outcome,
    string? ResolutionJustification,
    int? ResolvedByEmployeeId,
    DateTime? ResolvedAtUtc,
    int? ReplacementDeductionId);

public sealed record DeductionDto(
    int Id,
    DeductionKind Kind,
    DeductionStatus Status,
    int EmployeeId,
    string EmployeeName,
    int BugId,
    string BugDescription,
    int TaskId,
    string TaskTitle,
    int ModuleId,
    string ModuleName,
    int TimeLogId,
    DateOnly WorkDate,
    string SourceMonth,
    string ApplyMonth,
    bool RoutedFromLockedMonth,
    decimal Hours,
    decimal HourlyRateUsed,
    decimal SharePercent,
    decimal Amount,
    int? ReversesDeductionId,
    bool AmountSetByDispute,
    DateTime CreatedAtUtc,
    DateTime? NotifiedAtUtc,
    DateTime? DisputeWindowEndsAtUtc,
    bool DisputeWindowOpen,
    DateTime? AcknowledgedAtUtc,
    int? ApprovedByEmployeeId,
    DateTime? ApprovedAtUtc,
    int? PayrollRunId,
    string? CancellationReason,
    string? Note,
    DeductionDisputeDto? Dispute,
    int Version,
    bool ApprovableNow);

public sealed record DeductionListQuery(int? EmployeeId, DeductionStatus? Status, string? ApplyMonth, int? BugId, bool? OpenDispute);

public sealed record DeductionIdsRequest(IReadOnlyList<int> DeductionIds);

public sealed record RaiseDisputeRequest(string Reason);

public sealed record ResolveDisputeRequest(DisputeOutcome Outcome, string Justification, decimal? NewHours, decimal? NewSharePercent);

public sealed record ReasonRequest(string Reason);

internal sealed class DeductionIdsRequestValidator : AbstractValidator<DeductionIdsRequest>
{
    public DeductionIdsRequestValidator()
    {
        RuleFor(x => x.DeductionIds).NotEmpty().Must(ids => ids.Count <= 200).WithMessage("At most 200 lines per request.");
        RuleForEach(x => x.DeductionIds).GreaterThan(0);
        RuleFor(x => x.DeductionIds).Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Each line may appear only once.");
    }
}

internal sealed class RaiseDisputeRequestValidator : AbstractValidator<RaiseDisputeRequest>
{
    public RaiseDisputeRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .Must(r => r.Trim().Length >= DeductionDispute.MinimumReasonLength)
            .WithMessage($"A reason of at least {DeductionDispute.MinimumReasonLength} characters is required.")
            .MaximumLength(DeductionDispute.MaximumReasonLength);
    }
}

internal sealed class ResolveDisputeRequestValidator : AbstractValidator<ResolveDisputeRequest>
{
    public ResolveDisputeRequestValidator()
    {
        RuleFor(x => x.Outcome).IsInEnum();
        RuleFor(x => x.Justification)
            .NotEmpty()
            .Must(r => r.Trim().Length >= DeductionDispute.MinimumReasonLength)
            .WithMessage($"A justification of at least {DeductionDispute.MinimumReasonLength} characters is required.")
            .MaximumLength(DeductionDispute.MaximumReasonLength);
        RuleFor(x => x.NewHours).GreaterThan(0).PrecisionScale(6, 2, ignoreTrailingZeros: true).When(x => x.NewHours is not null);
        RuleFor(x => x.NewSharePercent).GreaterThan(0).LessThanOrEqualTo(100).PrecisionScale(5, 2, ignoreTrailingZeros: true).When(x => x.NewSharePercent is not null);
        RuleFor(x => x).Must(x => x.NewHours is null && x.NewSharePercent is null)
            .When(x => x.Outcome == DisputeOutcome.Rejected)
            .WithName("Outcome")
            .WithMessage("New hours or share can only be given when the dispute is upheld.");
    }
}

internal sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .Must(r => r.Trim().Length >= DeductionDispute.MinimumReasonLength)
            .WithMessage($"A reason of at least {DeductionDispute.MinimumReasonLength} characters is required.")
            .MaximumLength(500);
    }
}
