using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Domain.Policies;

/// <summary>
/// Every policy value a computation depends on, read once. A payroll run stores the snapshot it used,
/// so later policy changes never alter a computed run.
/// </summary>
public sealed record PolicySnapshot
{
    public PolicySnapshot(
        decimal standardHoursPerMonth,
        decimal dailyHours,
        decimal deductionCapPercent,
        int defaultWarrantyDays,
        int disputeWindowDays,
        bool qaMissDeductible,
        decimal qaMissSharePercent,
        bool requireEmployeeAcknowledgement)
    {
        Deduction = new DeductionPolicy(standardHoursPerMonth, dailyHours, deductionCapPercent);
        if (defaultWarrantyDays is < 0 or > 3650)
        {
            throw new DomainException("policy.invalid_warranty", "Warranty days must be between 0 and 3650.");
        }

        if (disputeWindowDays is < 1 or > 30)
        {
            throw new DomainException("policy.invalid_dispute_window", "The dispute window must be between 1 and 30 working days.");
        }

        if (qaMissSharePercent is < 0 or > 100 || !Precision.HasAtMostTwoDecimals(qaMissSharePercent))
        {
            throw new DomainException("policy.invalid_qa_share", "The QA-miss share must be between 0 and 100 with at most two decimals.");
        }

        DefaultWarrantyDays = defaultWarrantyDays;
        DisputeWindowDays = disputeWindowDays;
        QaMissDeductible = qaMissDeductible;
        QaMissSharePercent = qaMissSharePercent;
        RequireEmployeeAcknowledgement = requireEmployeeAcknowledgement;
    }

    public DeductionPolicy Deduction { get; }

    public decimal StandardHoursPerMonth => Deduction.StandardHoursPerMonth;

    public decimal DailyHours => Deduction.DailyHours;

    public decimal DeductionCapPercent => Deduction.DeductionCapPercent;

    public int DefaultWarrantyDays { get; }

    public int DisputeWindowDays { get; }

    public bool QaMissDeductible { get; }

    public decimal QaMissSharePercent { get; }

    public bool RequireEmployeeAcknowledgement { get; }
}
