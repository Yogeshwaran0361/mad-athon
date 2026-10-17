using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Jobs;

/// <summary>
/// Nightly: closes completed tasks whose warranty window has ended and whose bugs are all verified. A closed task can
/// still be reopened by a bug report; the bug is then outside warranty and not deductible. Safe to repeat.
/// </summary>
public sealed class WarrantySweepJob
{
    public const int BatchSize = 200;

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;

    public WarrantySweepJob(IApplicationDbContext db, IClock clock, IAuditWriter audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var today = WorkTimeZone.ToWorkDate(now);

        // Light projection first: which completed tasks are past warranty with nothing left to verify.
        var candidates = (await _db.WorkTasks.AsNoTracking()
                .Where(t => t.Status == WorkTaskStatus.Completed && t.ActualCompletionDate != null
                            && !t.Bugs.Any(b => b.Status == BugStatus.Open || b.Status == BugStatus.Fixed))
                .Select(t => new { t.Id, Completed = t.ActualCompletionDate!.Value, t.Module.Project.WarrantyDays })
                .ToListAsync(cancellationToken))
            .Where(t => Domain.Lifecycle.WarrantyPolicy.HasExpired(t.Completed, t.WarrantyDays, today))
            .Select(t => t.Id)
            .ToList();

        var closed = 0;
        foreach (var batch in candidates.Chunk(BatchSize))
        {
            await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
            var tasks = await _db.WorkTasks
                .Include(t => t.Module).ThenInclude(m => m.Project)
                .Include(t => t.History)
                .Include(t => t.Bugs)
                .Where(t => batch.Contains(t.Id))
                .ToListAsync(cancellationToken);
            foreach (var task in tasks)
            {
                var before = task.History.Count;
                if (task.CloseAfterWarranty(task.Module.Project.WarrantyDays, now) is not null)
                {
                    TaskAudit.RecordTransitions(_audit, task, before);
                    closed++;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return closed;
    }
}
