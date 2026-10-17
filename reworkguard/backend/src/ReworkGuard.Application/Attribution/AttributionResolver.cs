using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Attribution;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Attribution;

/// <summary>
/// Works out each responsible person's share of a bug: a manager override when one exists, otherwise the split
/// computed from hours. For an ordinary bug the people are the task's original deliverers, weighted by approved
/// regular hours; for a regression they are whoever delivered the earlier fix, weighted by approved corrective hours
/// on that earlier bug (falling back to the original deliverers if nobody logged fix hours).
/// </summary>
public sealed class AttributionResolver
{
    private readonly IApplicationDbContext _db;

    public AttributionResolver(IApplicationDbContext db)
    {
        _db = db;
    }

    public sealed record ResolvedShares(
        AttributionSource Source,
        AttributionBasis Basis,
        IReadOnlyList<AttributionShare> Shares,
        IReadOnlyDictionary<int, decimal> BasisHours);

    public async Task<IReadOnlyDictionary<int, ResolvedShares>> ResolveAsync(IReadOnlyCollection<int> bugIds, CancellationToken cancellationToken)
    {
        var bugs = await _db.Bugs.AsNoTracking()
            .Where(b => bugIds.Contains(b.Id))
            .Select(b => new { b.Id, b.TaskId, b.RegressionOfBugId })
            .ToListAsync(cancellationToken);
        var earlierBugIds = bugs.Where(b => b.RegressionOfBugId != null).Select(b => b.RegressionOfBugId!.Value).Distinct().ToList();
        var fixHours = await _db.TimeLogs.AsNoTracking()
            .Where(l => earlierBugIds.Contains(l.BugId!.Value) && l.WorkType == WorkType.Corrective && l.ApprovalStatus == ApprovalStatus.Approved)
            .GroupBy(l => new { BugId = l.BugId!.Value, l.EmployeeId })
            .Select(g => new { g.Key.BugId, g.Key.EmployeeId, Hours = g.Sum(l => l.Hours) })
            .ToListAsync(cancellationToken);
        var taskIds = bugs.Select(b => b.TaskId).Distinct().ToList();

        var deliverers = await _db.TaskAssignments.AsNoTracking()
            .Where(a => taskIds.Contains(a.TaskId) && a.IsOriginalDeliverer)
            .Select(a => new { a.TaskId, a.EmployeeId })
            .ToListAsync(cancellationToken);

        var regularHours = await _db.TimeLogs.AsNoTracking()
            .Where(l => taskIds.Contains(l.TaskId) && l.WorkType == WorkType.Regular && l.ApprovalStatus == ApprovalStatus.Approved)
            .GroupBy(l => new { l.TaskId, l.EmployeeId })
            .Select(g => new { g.Key.TaskId, g.Key.EmployeeId, Hours = g.Sum(l => l.Hours) })
            .ToListAsync(cancellationToken);

        var overrides = await _db.AttributionShareOverrides.AsNoTracking()
            .Where(o => bugIds.Contains(o.BugId))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, ResolvedShares>();
        foreach (var bug in bugs)
        {
            var fixers = fixHours.Where(h => h.BugId == bug.RegressionOfBugId).ToDictionary(h => h.EmployeeId, h => h.Hours);
            var basis = fixers.Count > 0 ? AttributionBasis.FixHoursOnEarlierBug : AttributionBasis.RegularHours;
            var hours = basis == AttributionBasis.FixHoursOnEarlierBug
                ? fixers
                : deliverers
                    .Where(d => d.TaskId == bug.TaskId)
                    .ToDictionary(
                        d => d.EmployeeId,
                        d => regularHours.SingleOrDefault(h => h.TaskId == bug.TaskId && h.EmployeeId == d.EmployeeId)?.Hours ?? 0m);

            var manual = overrides.Where(o => o.BugId == bug.Id).OrderBy(o => o.EmployeeId).ToList();
            result[bug.Id] = manual.Count > 0
                ? new ResolvedShares(AttributionSource.ManagerOverride, basis, manual.Select(o => new AttributionShare(o.EmployeeId, o.SharePercent)).ToList(), hours)
                : new ResolvedShares(
                    AttributionSource.Computed,
                    basis,
                    AttributionCalculator.ComputeShares(hours.Select(h => new DelivererHours(h.Key, h.Value)).ToList()),
                    hours);
        }

        return result;
    }
}
