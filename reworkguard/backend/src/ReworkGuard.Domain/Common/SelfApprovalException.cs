namespace ReworkGuard.Domain.Common;

public sealed class SelfApprovalException : DomainForbiddenException
{
    public SelfApprovalException()
        : base("timelog.self_approval", "You cannot approve or reject your own time log.")
    {
    }
}
