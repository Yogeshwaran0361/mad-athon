using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Payroll;

namespace ReworkGuard.Application.Bugs;

/// <summary>A manager's root-cause verdict, which decides whether a defect can lead to a payroll deduction.</summary>
public sealed class DefectReviewService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<ReviewBugRequest> _validator;
    private readonly PolicyProvider _policies;
    private readonly DeductionLedger _ledger;

    public DefectReviewService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IValidator<ReviewBugRequest> validator,
        PolicyProvider policies,
        DeductionLedger ledger)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _validator = validator;
        _policies = policies;
        _ledger = ledger;
    }

    /// <summary>Records the verdict and, in the same transaction, drafts or cancels the deduction lines it implies.</summary>
    public async Task<BugDto> ReviewAsync(int bugId, ReviewBugRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);
        var qaMissDeductible = (await _policies.GetSnapshotAsync(cancellationToken)).QaMissDeductible;

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var bug = await _db.Bugs.Include(b => b.Task).SingleOrDefaultAsync(b => b.Id == bugId, cancellationToken)
            ?? throw new NotFoundException("Bug", bugId);

        var previous = bug.IsReviewed
            ? new { bug.RootCause, bug.CausedByOriginalDelivery, bug.ReviewJustification, bug.RegressionOfBugId, IsDeductible = bug.IsDeductible(qaMissDeductible) }
            : null;

        bug.Review(request.RootCause, request.CausedByOriginalDelivery, request.Justification, _user.EmployeeId, _clock.UtcNow);
        if (request.RegressionOfBugId is { } earlierId)
        {
            var earlier = await _db.Bugs.AsNoTracking().SingleOrDefaultAsync(b => b.Id == earlierId, cancellationToken)
                ?? throw new NotFoundException("Bug", earlierId);
            bug.MarkRegressionOf(earlier);
        }

        _audit.Record(AuditEntities.Bug, bug.Id.ToString(CultureInfo.InvariantCulture), previous is null ? "Reviewed" : "ReReviewed", new
        {
            bug.RootCause,
            bug.CausedByOriginalDelivery,
            Justification = bug.ReviewJustification,
            bug.RegressionOfBugId,
            bug.WithinWarranty,
            Deductible = bug.IsDeductible(qaMissDeductible),
            Previous = previous,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await _ledger.SyncBugsAsync([bug.Id], previous is null ? "defect review" : "defect re-review", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BugService.ToDto(bug, bug.Task, qaMissDeductible);
    }
}
