using FluentValidation;

namespace ReworkGuard.Application.Attribution;

public enum AttributionSource
{
    Computed,
    ManagerOverride,
}

public enum AttributionBasis
{
    /// <summary>The task's original deliverers, by approved regular hours.</summary>
    RegularHours,

    /// <summary>A regression: whoever delivered the earlier fix, by approved corrective hours on that bug.</summary>
    FixHoursOnEarlierBug,
}

public sealed record AttributionShareDto(int EmployeeId, string EmployeeName, decimal BasisHours, decimal SharePercent);

public sealed record AttributionDto(
    int BugId,
    int TaskId,
    bool IsDeductible,
    AttributionSource Source,
    AttributionBasis Basis,
    int? RegressionOfBugId,
    IReadOnlyList<AttributionShareDto> Shares,
    string? OverrideJustification,
    int? OverriddenByEmployeeId,
    DateTime? OverriddenAtUtc);

public sealed record ShareInput(int EmployeeId, decimal SharePercent);

public sealed record OverrideAttributionRequest(IReadOnlyList<ShareInput> Shares, string Justification);

internal sealed class OverrideAttributionRequestValidator : AbstractValidator<OverrideAttributionRequest>
{
    public OverrideAttributionRequestValidator()
    {
        RuleFor(x => x.Shares).NotEmpty();
        RuleForEach(x => x.Shares).ChildRules(share =>
        {
            share.RuleFor(s => s.EmployeeId).GreaterThan(0);
            share.RuleFor(s => s.SharePercent).GreaterThan(0).LessThanOrEqualTo(100);
        });
        RuleFor(x => x.Justification).NotEmpty().Must(j => j.Trim().Length >= 10).WithMessage("A justification of at least 10 characters is required.").MaximumLength(2000);
    }
}
