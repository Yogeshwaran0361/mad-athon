using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>Append-only record of one task status change.</summary>
public class TaskStateHistory : IAppendOnly
{
    private TaskStateHistory()
    {
    }

    internal TaskStateHistory(WorkTaskStatus fromStatus, WorkTaskStatus toStatus, int? changedByEmployeeId, DateTime changedAtUtc, string? reason)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedByEmployeeId = changedByEmployeeId;
        ChangedAtUtc = changedAtUtc;
        Reason = reason;
    }

    public long Id { get; private set; }

    public int TaskId { get; private set; }

    public WorkTaskStatus FromStatus { get; private set; }

    public WorkTaskStatus ToStatus { get; private set; }

    /// <summary>Who made the change; null when the system did (e.g. the warranty sweep closing a task).</summary>
    public int? ChangedByEmployeeId { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    public string? Reason { get; private set; }
}
