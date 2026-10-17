using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// An employee's share of the profit pool of a closed change request. A manager approves it (never their own); once
/// approved it is paid as a separate earning line in the payroll of its apply month or the first open month after.
/// </summary>
public class IncentiveAward : IVersioned
{
    private IncentiveAward()
    {
    }

    public IncentiveAward(int changeRequestId, int employeeId, decimal hours, decimal sharePercent, decimal amount, YearMonth applyMonth, DateTime utcNow)
    {
        if (amount < 0 || !Precision.HasAtMostTwoDecimals(amount) || sharePercent is < 0 or > 100)
        {
            throw new DomainException("award.invalid_amount", "An award needs a share between 0 and 100 and a non-negative amount with at most two decimals.");
        }

        ChangeRequestId = changeRequestId;
        EmployeeId = employeeId;
        Hours = hours;
        SharePercent = sharePercent;
        Amount = amount;
        ApplyMonth = applyMonth.ToString();
        Status = IncentiveAwardStatus.Pending;
        CreatedAtUtc = utcNow;
    }

    public int Id { get; private set; }

    public int ChangeRequestId { get; private set; }

    public int EmployeeId { get; private set; }

    /// <summary>The employee's approved Enhancement hours on the request.</summary>
    public decimal Hours { get; private set; }

    public decimal SharePercent { get; private set; }

    public decimal Amount { get; private set; }

    public IncentiveAwardStatus Status { get; private set; }

    /// <summary>The pay month (yyyy-MM) the award is first due in.</summary>
    public string ApplyMonth { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public int? DecidedByEmployeeId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    public int? PayrollRunId { get; private set; }

    public int Version { get; set; }

    public void Approve(int approverId, DateTime utcNow) => Decide(IncentiveAwardStatus.Approved, approverId, null, utcNow);

    public void Reject(int approverId, string reason, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("award.reason_required", "Give a reason for rejecting the award.");
        }

        Decide(IncentiveAwardStatus.Rejected, approverId, reason.Trim(), utcNow);
    }

    public void MarkPaid(int payrollRunId)
    {
        if (Status != IncentiveAwardStatus.Approved || PayrollRunId is not null)
        {
            throw new DomainException("award.not_payable", $"Award {Id} is {Status} and cannot be paid.");
        }

        Status = IncentiveAwardStatus.Paid;
        PayrollRunId = payrollRunId;
    }

    private void Decide(IncentiveAwardStatus decision, int approverId, string? reason, DateTime utcNow)
    {
        if (approverId == EmployeeId)
        {
            throw new DomainForbiddenException("award.self_approval", "You cannot approve or reject your own award.");
        }

        if (Status != IncentiveAwardStatus.Pending)
        {
            throw new DomainException("award.already_decided", $"This award is already {Status}.");
        }

        Status = decision;
        DecidedByEmployeeId = approverId;
        DecidedAtUtc = utcNow;
        RejectionReason = reason;
    }
}
