namespace ReworkGuard.Domain.Enums;

public enum IncentiveAwardStatus
{
    Pending,
    Approved,
    Rejected,

    /// <summary>Taken by a finalized payroll run as an earning line.</summary>
    Paid,
}
