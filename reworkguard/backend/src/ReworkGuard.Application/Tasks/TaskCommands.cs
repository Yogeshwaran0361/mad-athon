using FluentValidation;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Tasks;

public sealed record CreateTaskRequest(int ModuleId, string Title, DateOnly CommittedDate, IReadOnlyList<int> AssigneeIds);

public sealed record TransitionTaskRequest(WorkTaskStatus TargetStatus, string? Reason);

public sealed record TaskListQuery(WorkTaskStatus? Status, int? AssigneeId);

internal sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.ModuleId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.CommittedDate).NotEmpty();
        RuleFor(x => x.AssigneeIds).NotEmpty().Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Assignees must be distinct.");
        RuleForEach(x => x.AssigneeIds).GreaterThan(0);
    }
}

internal sealed class TransitionTaskRequestValidator : AbstractValidator<TransitionTaskRequest>
{
    public TransitionTaskRequestValidator()
    {
        RuleFor(x => x.TargetStatus).IsInEnum();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
