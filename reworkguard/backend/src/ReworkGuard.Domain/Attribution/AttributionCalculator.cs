using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Attribution;

public sealed record DelivererHours(int EmployeeId, decimal ApprovedRegularHours);

public sealed record AttributionShare(int EmployeeId, decimal SharePercent);

/// <summary>Splits responsibility for a defect between the task's original deliverers.</summary>
public static class AttributionCalculator
{
    public const decimal FullShare = 100.00m;

    /// <summary>
    /// Each deliverer's share is proportional to their approved regular hours on the task; with no hours the split is equal.
    /// Shares are rounded to two decimals and always sum to exactly 100.00: the rounding remainder goes to the
    /// largest share (lowest employee id on a tie).
    /// </summary>
    public static IReadOnlyList<AttributionShare> ComputeShares(IReadOnlyList<DelivererHours> deliverers)
    {
        if (deliverers.Count == 0)
        {
            throw new DomainException("attribution.no_deliverers", "The task has no original deliverers to attribute the defect to.");
        }

        if (deliverers.Select(d => d.EmployeeId).Distinct().Count() != deliverers.Count)
        {
            throw new DomainException("attribution.duplicate_deliverer", "Each deliverer may appear only once.");
        }

        if (deliverers.Any(d => d.ApprovedRegularHours < 0))
        {
            throw new DomainException("attribution.negative_hours", "Approved hours cannot be negative.");
        }

        var totalHours = deliverers.Sum(d => d.ApprovedRegularHours);
        var ordered = deliverers.OrderBy(d => d.EmployeeId).ToList();

        var shares = ordered
            .Select(d => new AttributionShare(
                d.EmployeeId,
                Precision.Round2(totalHours == 0
                    ? FullShare / ordered.Count
                    : d.ApprovedRegularHours / totalHours * FullShare)))
            .ToList();

        var remainder = FullShare - shares.Sum(s => s.SharePercent);
        if (remainder != 0)
        {
            var largest = shares.OrderByDescending(s => s.SharePercent).ThenBy(s => s.EmployeeId).First();
            var index = shares.IndexOf(largest);
            shares[index] = largest with { SharePercent = largest.SharePercent + remainder };
        }

        return shares;
    }

    /// <summary>
    /// Checks a manager's manual split: only the people responsible (the original deliverers, or the fix deliverers for a
    /// regression), positive two-decimal shares, total exactly 100.00.
    /// </summary>
    public static void ValidateOverride(IReadOnlyList<AttributionShare> shares, IEnumerable<int> responsibleIds)
    {
        var allowed = responsibleIds.ToHashSet();
        if (shares.Count == 0)
        {
            throw new DomainException("attribution.empty", "At least one share is required.");
        }

        if (shares.Select(s => s.EmployeeId).Distinct().Count() != shares.Count)
        {
            throw new DomainException("attribution.duplicate_deliverer", "Each deliverer may appear only once.");
        }

        if (shares.Any(s => !allowed.Contains(s.EmployeeId)))
        {
            throw new DomainException("attribution.not_original_deliverer", "Shares can only be assigned to the people responsible: the original deliverers, or for a regression the people who delivered the earlier fix.");
        }

        if (shares.Any(s => s.SharePercent <= 0 || s.SharePercent > FullShare || !Precision.HasAtMostTwoDecimals(s.SharePercent)))
        {
            throw new DomainException("attribution.invalid_share", "Each share must be greater than 0 and at most 100, with at most two decimals.");
        }

        if (shares.Sum(s => s.SharePercent) != FullShare)
        {
            throw new DomainException("attribution.not_100", "Shares must add up to exactly 100.00.");
        }
    }
}
