using FluentValidation;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.TimeLogs;

public sealed record LogTimeRequest(int TaskId, DateOnly WorkDate, decimal Hours, WorkType WorkType, int? BugId, string? Note);

public sealed record RejectTimeLogRequest(string? Reason);

public sealed record TimeLogListQuery(int? EmployeeId, int? TaskId, ApprovalStatus? ApprovalStatus, WorkType? WorkType);

internal sealed class LogTimeRequestValidator : AbstractValidator<LogTimeRequest>
{
    public LogTimeRequestValidator()
    {
        RuleFor(x => x.TaskId).GreaterThan(0);
        RuleFor(x => x.WorkDate).NotEmpty();
        RuleFor(x => x.Hours)
            .GreaterThan(0)
            .LessThanOrEqualTo(TimeLog.MaxHoursPerDay)
            .Must(Precision.HasAtMostTwoDecimals).WithMessage("Hours can have at most two decimals.");
        RuleFor(x => x.WorkType).IsInEnum();
        RuleFor(x => x.BugId)
            .NotNull().GreaterThan(0).When(x => x.WorkType == WorkType.Corrective)
            .WithMessage("Corrective hours must reference the bug being fixed.");
        RuleFor(x => x.BugId)
            .Null().When(x => x.WorkType != WorkType.Corrective)
            .WithMessage("Only corrective hours can reference a bug.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

internal sealed class RejectTimeLogRequestValidator : AbstractValidator<RejectTimeLogRequest>
{
    public RejectTimeLogRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
