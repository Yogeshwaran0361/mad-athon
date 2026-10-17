using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Common;

public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(WorkTaskStatus from, WorkTaskStatus to, string reason)
        : base("task.invalid_transition", $"Task cannot move from {from} to {to}: {reason}")
    {
        From = from;
        To = to;
    }

    public WorkTaskStatus From { get; }

    public WorkTaskStatus To { get; }
}
