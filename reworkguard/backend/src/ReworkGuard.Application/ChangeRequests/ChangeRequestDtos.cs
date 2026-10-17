using FluentValidation;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.ChangeRequests;

public sealed record CreateChangeRequestRequest(int ProjectId, int ModuleId, string Title, string CustomerName, string ClientReference, decimal QuotedAmountExTax);

/// <summary>Approval sizes the work and names who does it; a new task linked to the request is created for them.</summary>
public sealed record ApproveChangeRequestRequest(decimal ApprovedHours, IReadOnlyList<int> AssigneeIds, DateOnly? CommittedDate);

public sealed record RejectChangeRequestRequest(string Reason);

public sealed record CloseChangeRequestRequest(decimal ReceivedAmountExTax);

/// <summary>Money fields are null for roles that may not see revenue (everyone except Manager, Finance and Admin).</summary>
public sealed record ChangeRequestDto(
    int Id,
    int ProjectId,
    string ProjectName,
    int ModuleId,
    string ModuleName,
    string Title,
    string CustomerName,
    string ClientReference,
    decimal? QuotedAmountExTax,
    decimal ApprovedHours,
    decimal? ReceivedAmountExTax,
    ChangeRequestStatus Status,
    DateTime CreatedAtUtc,
    int? ApprovedByEmployeeId,
    string? RejectionReason,
    int? TaskId,
    int Version);

internal sealed class CreateChangeRequestRequestValidator : AbstractValidator<CreateChangeRequestRequest>
{
    public CreateChangeRequestRequestValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.ModuleId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ClientReference).NotEmpty().MaximumLength(100);
        RuleFor(x => x.QuotedAmountExTax).GreaterThanOrEqualTo(0);
    }
}

internal sealed class ApproveChangeRequestRequestValidator : AbstractValidator<ApproveChangeRequestRequest>
{
    public ApproveChangeRequestRequestValidator()
    {
        RuleFor(x => x.AssigneeIds).NotEmpty().Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Assignees must be distinct.");
        RuleForEach(x => x.AssigneeIds).GreaterThan(0);
    }
}

internal sealed class RejectChangeRequestRequestValidator : AbstractValidator<RejectChangeRequestRequest>
{
    public RejectChangeRequestRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class CloseChangeRequestRequestValidator : AbstractValidator<CloseChangeRequestRequest>
{
    public CloseChangeRequestRequestValidator()
    {
        RuleFor(x => x.ReceivedAmountExTax).GreaterThanOrEqualTo(0);
    }
}
