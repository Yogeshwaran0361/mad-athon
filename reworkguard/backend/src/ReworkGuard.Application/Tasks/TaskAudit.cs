using System.Globalization;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Tasks;

internal static class TaskAudit
{
    /// <summary>Writes one audit event per status change the task recorded after <paramref name="historyCountBefore"/>.</summary>
    public static void RecordTransitions(IAuditWriter audit, WorkTask task, int historyCountBefore)
    {
        foreach (var change in task.History.Skip(historyCountBefore))
        {
            audit.Record(
                AuditEntities.Task,
                task.Id.ToString(CultureInfo.InvariantCulture),
                "StatusChanged",
                new { change.FromStatus, change.ToStatus, change.Reason, task.ReopenCount });
        }
    }
}
