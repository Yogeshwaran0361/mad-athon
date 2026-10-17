using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// A customer's request for additional features on a delivered project: new paid business, not a defect. It never
/// reopens the original task; approving it creates a fresh task for the work, logged as Enhancement hours.
/// </summary>
public class ChangeRequest : IVersioned
{
    private static readonly Dictionary<ChangeRequestStatus, ChangeRequestStatus[]> Allowed = new()
    {
        [ChangeRequestStatus.Requested] = [ChangeRequestStatus.Approved, ChangeRequestStatus.Rejected],
        [ChangeRequestStatus.Approved] = [ChangeRequestStatus.InProgress],
        [ChangeRequestStatus.InProgress] = [ChangeRequestStatus.Completed],
        [ChangeRequestStatus.Completed] = [ChangeRequestStatus.Closed],
        [ChangeRequestStatus.Closed] = [],
        [ChangeRequestStatus.Rejected] = [],
    };

    private ChangeRequest()
    {
    }

    public ChangeRequest(int projectId, int moduleId, string title, string customerName, string clientReference, decimal quotedAmountExTax, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(clientReference))
        {
            throw new DomainException("cr.client_reference_required", "A change request needs the client's reference.");
        }

        if (quotedAmountExTax < 0 || !Precision.HasAtMostTwoDecimals(quotedAmountExTax))
        {
            throw new DomainException("cr.invalid_amount", "The quoted amount must be zero or more with at most two decimals.");
        }

        ProjectId = projectId;
        ModuleId = moduleId;
        Title = title.Trim();
        CustomerName = customerName.Trim();
        ClientReference = clientReference.Trim();
        QuotedAmountExTax = quotedAmountExTax;
        Status = ChangeRequestStatus.Requested;
        CreatedAtUtc = utcNow;
    }

    public int Id { get; private set; }

    public int ProjectId { get; private set; }

    public int ModuleId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string ClientReference { get; private set; } = string.Empty;

    public decimal QuotedAmountExTax { get; private set; }

    public decimal ApprovedHours { get; private set; }

    public decimal ReceivedAmountExTax { get; private set; }

    public ChangeRequestStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public int? ApprovedByEmployeeId { get; private set; }

    public string? RejectionReason { get; private set; }

    public int Version { get; set; }

    /// <summary>Profit-share figures, fixed once when the request is closed. Visible to Manager, Finance and Admin only.</summary>
    public decimal? LabourCostExTax { get; private set; }

    public decimal? ProfitExTax { get; private set; }

    public decimal? ProfitSharePool { get; private set; }

    public DateTime? ProfitShareSettledAtUtc { get; private set; }

    /// <summary>Enhancement hours can be logged only while the work is approved or under way.</summary>
    public bool AcceptsEnhancementHours => Status is ChangeRequestStatus.Approved or ChangeRequestStatus.InProgress;

    public void Approve(int approverId, decimal approvedHours)
    {
        if (approvedHours <= 0 || !Precision.HasAtMostTwoDecimals(approvedHours))
        {
            throw new DomainException("cr.invalid_hours", "Approved hours must be greater than 0 with at most two decimals.");
        }

        MoveTo(ChangeRequestStatus.Approved);
        ApprovedHours = approvedHours;
        ApprovedByEmployeeId = approverId;
    }

    public void Reject(string reason)
    {
        MoveTo(ChangeRequestStatus.Rejected);
        RejectionReason = reason.Trim();
    }

    public void Start() => MoveTo(ChangeRequestStatus.InProgress);

    public void Complete() => MoveTo(ChangeRequestStatus.Completed);

    /// <summary>
    /// Keeps the request in step with its task: the task starting starts the request, the task being delivered
    /// completes it. Returns the audit actions taken (none when the request is already there or past it).
    /// </summary>
    public IReadOnlyList<string> FollowTask(WorkTaskStatus taskStatus)
    {
        var actions = new List<string>();
        if ((taskStatus is WorkTaskStatus.InProgress or WorkTaskStatus.Completed) && Status == ChangeRequestStatus.Approved)
        {
            Start();
            actions.Add("Started");
        }

        if (taskStatus == WorkTaskStatus.Completed && Status == ChangeRequestStatus.InProgress)
        {
            Complete();
            actions.Add("Completed");
        }

        return actions;
    }

    public void Close(decimal receivedAmountExTax)
    {
        if (receivedAmountExTax < 0 || !Precision.HasAtMostTwoDecimals(receivedAmountExTax))
        {
            throw new DomainException("cr.invalid_amount", "The received amount must be zero or more with at most two decimals.");
        }

        MoveTo(ChangeRequestStatus.Closed);
        ReceivedAmountExTax = receivedAmountExTax;
    }

    /// <summary>Records the settlement. Happens once: a second settlement would duplicate awards.</summary>
    public void RecordProfitShare(decimal labourCost, decimal profit, decimal pool, DateTime utcNow)
    {
        if (Status != ChangeRequestStatus.Closed)
        {
            throw new DomainException("cr.not_closed", "Profit share is settled only when the change request is closed.");
        }

        if (ProfitShareSettledAtUtc is not null)
        {
            throw new DomainException("cr.profit_share_settled", "Profit share for this change request is already settled.");
        }

        LabourCostExTax = labourCost;
        ProfitExTax = profit;
        ProfitSharePool = pool;
        ProfitShareSettledAtUtc = utcNow;
    }

    public static bool CanMove(ChangeRequestStatus from, ChangeRequestStatus to) => Allowed[from].Contains(to);

    private void MoveTo(ChangeRequestStatus target)
    {
        if (!CanMove(Status, target))
        {
            throw new DomainException("cr.invalid_transition", $"Change request cannot move from {Status} to {target}.");
        }

        Status = target;
    }
}
