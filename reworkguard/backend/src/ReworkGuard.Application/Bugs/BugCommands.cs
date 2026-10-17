using FluentValidation;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Bugs;

public sealed record ReportBugRequest(BugSeverity Severity, string Description, DateOnly FoundDate);

public sealed record BugListQuery(BugStatus? Status, int? TaskId, bool? Reviewed);

internal sealed class ReportBugRequestValidator : AbstractValidator<ReportBugRequest>
{
    public ReportBugRequestValidator()
    {
        RuleFor(x => x.Severity).IsInEnum();
        RuleFor(x => x.Description).NotEmpty().MinimumLength(5).MaximumLength(4000);
        RuleFor(x => x.FoundDate).NotEmpty();
    }
}
