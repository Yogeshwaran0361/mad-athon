using FluentValidation;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;

namespace ReworkGuard.Application.Bugs;

public sealed record ReviewBugRequest(RootCause RootCause, bool CausedByOriginalDelivery, string? Justification, int? RegressionOfBugId = null);

internal sealed class ReviewBugRequestValidator : AbstractValidator<ReviewBugRequest>
{
    public ReviewBugRequestValidator()
    {
        RuleFor(x => x.RootCause).IsInEnum();
        RuleFor(x => x.Justification).MaximumLength(2000);
        RuleFor(x => x.RegressionOfBugId).GreaterThan(0).When(x => x.RegressionOfBugId is not null);
        RuleFor(x => x.Justification)
            .Must(j => (j?.Trim().Length ?? 0) >= DeductibilityPolicy.MinimumJustificationLength)
            .When(x => DeductibilityPolicy.RequiresJustification(x.RootCause, x.CausedByOriginalDelivery))
            .WithMessage($"A justification of at least {DeductibilityPolicy.MinimumJustificationLength} characters is required unless the defect is the deliverer's own and caused by the original delivery.");
    }
}
