using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Tests.Domain;

/// <summary>In-memory domain objects for pure unit tests (no database).</summary>
internal static class DomainFactory
{
    public const int OwnerId = 10;
    public const int SecondOwnerId = 11;
    public const int LeadId = 20;
    public const int QaId = 30;
    public const int ManagerId = 40;
    public const int WarrantyDays = 45;

    public static readonly DateTime Start = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Completed = new(2026, 1, 20, 17, 0, 0, DateTimeKind.Utc);

    public static WorkTask NewTask(params int[] ownerIds)
    {
        var project = new Project("P", WarrantyDays);
        var module = new Module(project, "M");
        return new WorkTask(module, "Task", new DateOnly(2026, 1, 20), ownerIds.Length == 0 ? [OwnerId] : ownerIds);
    }

    public static WorkTask TaskIn(WorkTaskStatus status)
    {
        var task = NewTask(OwnerId, SecondOwnerId);
        if (status == WorkTaskStatus.Assigned)
        {
            return task;
        }

        task.MoveTo(WorkTaskStatus.InProgress, OwnerId, null, Start);
        if (status == WorkTaskStatus.InProgress)
        {
            return task;
        }

        task.MoveTo(WorkTaskStatus.Completed, OwnerId, null, Completed);
        if (status == WorkTaskStatus.Completed)
        {
            return task;
        }

        if (status == WorkTaskStatus.Closed)
        {
            task.Close(ManagerId, actorIsManager: true, WarrantyDays, null, Completed.AddDays(1));
            return task;
        }

        task.ReportBug(WarrantyDays, BugSeverity.High, "Broken", DateOnly.FromDateTime(Completed.AddDays(3)), QaId, Completed.AddDays(3));
        if (status == WorkTaskStatus.Reopened)
        {
            return task;
        }

        task.MoveTo(WorkTaskStatus.UnderFix, OwnerId, null, Completed.AddDays(4));
        return task;
    }
}
