using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Attribution;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Attribution;

public sealed class AttributionService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly AttributionResolver _resolver;
    private readonly IValidator<OverrideAttributionRequest> _validator;
    private readonly PolicyProvider _policies;
    private readonly DeductionLedger _ledger;

    public AttributionService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        AttributionResolver resolver,
        IValidator<OverrideAttributionRequest> validator,
        PolicyProvider policies,
        DeductionLedger ledger)
    {
        _policies = policies;
        _ledger = ledger;
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _resolver = resolver;
        _validator = validator;
    }

    public async Task<AttributionDto> GetAsync(int bugId, CancellationToken cancellationToken)
    {
        var bug = await _db.Bugs.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bugId, cancellationToken)
            ?? throw new NotFoundException("Bug", bugId);

        var resolved = (await _resolver.ResolveAsync([bugId], cancellationToken))[bugId];
        var employeeIds = resolved.BasisHours.Keys.Union(resolved.Shares.Select(s => s.EmployeeId)).ToList();
        var names = await _db.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var overrides = await _db.AttributionShareOverrides.AsNoTracking().Where(o => o.BugId == bugId).ToListAsync(cancellationToken);
        var latest = overrides.MaxBy(o => o.SetAtUtc);
        var qaMissDeductible = (await _policies.GetSnapshotAsync(cancellationToken)).QaMissDeductible;

        return new AttributionDto(
            bug.Id,
            bug.TaskId,
            bug.IsDeductible(qaMissDeductible),
            resolved.Source,
            resolved.Basis,
            bug.RegressionOfBugId,
            resolved.Shares
                .Select(s => new AttributionShareDto(s.EmployeeId, names.GetValueOrDefault(s.EmployeeId, "Unknown"), resolved.BasisHours.GetValueOrDefault(s.EmployeeId), s.SharePercent))
                .ToList(),
            latest?.Justification,
            latest?.SetByEmployeeId,
            latest?.SetAtUtc);
    }

    /// <summary>Replaces the computed split with a manager's split. Justification is mandatory and the change is audited.</summary>
    public async Task<AttributionDto> OverrideAsync(int bugId, OverrideAttributionRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var bug = await _db.Bugs.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bugId, cancellationToken)
            ?? throw new NotFoundException("Bug", bugId);
        var before = (await _resolver.ResolveAsync([bugId], cancellationToken))[bugId];
        var shares = request.Shares.Select(s => new AttributionShare(s.EmployeeId, s.SharePercent)).ToList();
        AttributionCalculator.ValidateOverride(shares, before.BasisHours.Keys);

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);

        var justification = request.Justification.Trim();
        var existing = await _db.AttributionShareOverrides.Where(o => o.BugId == bugId).ToListAsync(cancellationToken);
        _db.AttributionShareOverrides.RemoveRange(existing);
        _db.AttributionShareOverrides.AddRange(shares.Select(s =>
            new AttributionShareOverride(bugId, s.EmployeeId, s.SharePercent, _user.EmployeeId, _clock.UtcNow, justification)));

        _audit.Record(AuditEntities.Bug, bugId.ToString(CultureInfo.InvariantCulture), "AttributionOverridden", new
        {
            Justification = justification,
            Before = new { before.Source, before.Shares },
            After = shares,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await _ledger.SyncBugsAsync([bugId], "attribution override", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(bugId, cancellationToken);
    }
}
