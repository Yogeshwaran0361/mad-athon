using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Lifecycle;

namespace ReworkGuard.Application.Bugs;

public sealed class BugService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<ReportBugRequest> _reportValidator;
    private readonly PolicyProvider _policies;
    private readonly NotificationPublisher _notifications;

    public BugService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IValidator<ReportBugRequest> reportValidator,
        PolicyProvider policies,
        NotificationPublisher notifications)
    {
        _notifications = notifications;
        _policies = policies;
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _reportValidator = reportValidator;
    }

    /// <summary>
    /// Reports a defect on a delivered task: stores the bug with its warranty verdict, reopens the task,
    /// and returns the original deliverers so the reporter can see who owns the fix.
    /// </summary>
    public async Task<BugReportedDto> ReportAsync(int taskId, ReportBugRequest request, CancellationToken cancellationToken)
    {
        await _reportValidator.ValidateAndThrowAsync(request, cancellationToken);

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var task = await _db.LoadAggregateAsync(taskId, cancellationToken);
        var historyBefore = task.History.Count;
        var warrantyDays = task.Module.Project.WarrantyDays;

        var bug = task.ReportBug(warrantyDays, request.Severity, request.Description.Trim(), request.FoundDate, _user.EmployeeId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AuditEntities.Bug, bug.Id.ToString(CultureInfo.InvariantCulture), "Reported", new
        {
            bug.TaskId,
            bug.Severity,
            bug.FoundDate,
            bug.WithinWarranty,
            TaskCompletionDate = task.ActualCompletionDate,
            WarrantyDays = warrantyDays,
            OriginalDelivererIds = task.OriginalDelivererIds.ToList(),
        });
        TaskAudit.RecordTransitions(_audit, task, historyBefore);
        await _notifications.PublishAsync(
            task.OriginalDelivererIds.Select(id => NotificationTemplates.BugReported(id, bug.Id, task.Id, task.Title, bug.WithinWarranty)).ToList(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var deliverers = await _db.TaskAssignments.AsNoTracking()
            .Where(a => a.TaskId == taskId && a.IsOriginalDeliverer)
            .Select(a => new AssigneeDto(a.EmployeeId, a.Employee.Name, a.IsOriginalDeliverer))
            .ToListAsync(cancellationToken);

        var completion = task.ActualCompletionDate!.Value;
        return new BugReportedDto(
            ToDto(bug, task, await QaMissDeductibleAsync(cancellationToken)),
            completion,
            WarrantyPolicy.LastWarrantyDay(completion, warrantyDays),
            task.ReopenCount,
            deliverers);
    }

    public async Task<BugDto> GetAsync(int bugId, CancellationToken cancellationToken)
    {
        var bug = await _db.Bugs.AsNoTracking().Include(b => b.Task).SingleOrDefaultAsync(b => b.Id == bugId, cancellationToken)
            ?? throw new NotFoundException("Bug", bugId);
        return ToDto(bug, bug.Task, await QaMissDeductibleAsync(cancellationToken));
    }

    public async Task<PagedResult<BugDto>> ListAsync(BugListQuery query, PageRequest page, CancellationToken cancellationToken)
    {
        var bugs = _db.Bugs.AsNoTracking().Include(b => b.Task).AsQueryable();
        if (query.Status is not null)
        {
            bugs = bugs.Where(b => b.Status == query.Status);
        }

        if (query.TaskId is not null)
        {
            bugs = bugs.Where(b => b.TaskId == query.TaskId);
        }

        if (query.Reviewed is not null)
        {
            bugs = bugs.Where(b => (b.ReviewedAtUtc != null) == query.Reviewed);
        }

        var result = await bugs.OrderByDescending(b => b.Id).ToPagedResultAsync(page, cancellationToken);
        var qaMissDeductible = await QaMissDeductibleAsync(cancellationToken);
        return new PagedResult<BugDto>(result.Items.Select(b => ToDto(b, b.Task, qaMissDeductible)).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    public Task<BugDto> VerifyAsync(int bugId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(bugId, "Verified", bug => bug.Verify(), cancellationToken);

    public Task<BugDto> CloseAsync(int bugId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(bugId, "Closed", bug => bug.Close(), cancellationToken);

    private async Task<BugDto> ChangeStatusAsync(int bugId, string action, Action<Bug> change, CancellationToken cancellationToken)
    {
        var bug = await _db.Bugs.Include(b => b.Task).SingleOrDefaultAsync(b => b.Id == bugId, cancellationToken)
            ?? throw new NotFoundException("Bug", bugId);
        var from = bug.Status;
        change(bug);
        _audit.Record(AuditEntities.Bug, bug.Id.ToString(CultureInfo.InvariantCulture), action, new { FromStatus = from, ToStatus = bug.Status });
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(bug, bug.Task, await QaMissDeductibleAsync(cancellationToken));
    }

    private async Task<bool> QaMissDeductibleAsync(CancellationToken cancellationToken) =>
        (await _policies.GetSnapshotAsync(cancellationToken)).QaMissDeductible;

    internal static BugDto ToDto(Bug b, WorkTask task, bool qaMissDeductible) => new(
        b.Id,
        task.Id,
        task.Title,
        task.Status,
        b.Severity,
        b.Description,
        b.FoundDate,
        b.ReportedByEmployeeId,
        b.Status,
        b.WithinWarranty,
        b.RootCause,
        b.CausedByOriginalDelivery,
        b.ReviewedByEmployeeId,
        b.ReviewJustification,
        b.ReviewedAtUtc,
        b.RegressionOfBugId,
        b.IsDeductible(qaMissDeductible),
        b.Version);
}
