using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Policies;

namespace ReworkGuard.Domain.Lifecycle;

public static class DeductibilityPolicy
{
    public const int MinimumJustificationLength = 10;

    /// <summary>
    /// A defect produces a payroll deduction only when it was found inside the warranty window and a manager confirmed
    /// it was caused by the original delivery with root cause OwnDefect. A QaMiss is deductible only when the
    /// QaMissDeductible policy is on.
    /// </summary>
    public static bool IsDeductible(bool withinWarranty, bool? causedByOriginalDelivery, RootCause? rootCause, bool qaMissDeductible) =>
        withinWarranty
        && causedByOriginalDelivery == true
        && (rootCause == RootCause.OwnDefect || (rootCause == RootCause.QaMiss && qaMissDeductible));

    public static bool IsDeductible(bool withinWarranty, bool? causedByOriginalDelivery, RootCause? rootCause, PolicySnapshot policy) =>
        IsDeductible(withinWarranty, causedByOriginalDelivery, rootCause, policy.QaMissDeductible);

    /// <summary>
    /// The share of a line actually deducted: the attribution share for an OwnDefect, and the attribution share scaled by
    /// QaMissSharePercent for a deductible QaMiss (the rest is treated as the testing gap's cost).
    /// </summary>
    public static decimal EffectiveSharePercent(decimal attributionShare, RootCause rootCause, PolicySnapshot policy) =>
        rootCause == RootCause.QaMiss ? Precision.Round2(attributionShare * policy.QaMissSharePercent / 100m) : attributionShare;

    /// <summary>A review that clears the deliverer, or names any cause other than their own defect, must be justified.</summary>
    public static bool RequiresJustification(RootCause rootCause, bool causedByOriginalDelivery) =>
        !causedByOriginalDelivery || rootCause != RootCause.OwnDefect;
}
