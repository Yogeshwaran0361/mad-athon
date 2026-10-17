using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Tasks;

/// <summary>Loads the task aggregate with everything its rules need.</summary>
internal static class TaskQueries
{
    public static async Task<WorkTask> LoadAggregateAsync(this IApplicationDbContext db, int taskId, CancellationToken cancellationToken) =>
        await db.WorkTasks
            .Include(t => t.Module).ThenInclude(m => m.Project)
            .Include(t => t.Assignments)
            .Include(t => t.History)
            .Include(t => t.Bugs)
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
        ?? throw new NotFoundException("Task", taskId);
}
