using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;

namespace ReworkGuard.Application.Tasks;

public sealed class TaskService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<CreateTaskRequest> _createValidator;
    private readonly IValidator<TransitionTaskRequest> _transitionValidator;
    private readonly Payroll.PolicyProvider _policies;

    public TaskService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IValidator<CreateTaskRequest> createValidator,
        IValidator<TransitionTaskRequest> transitionValidator,
        Payroll.PolicyProvider policies)
    {
        _policies = policies;
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _createValidator = createValidator;
        _transitionValidator = transitionValidator;
    }

    public async Task<PagedResult<TaskSummaryDto>> ListAsync(TaskListQuery query, PageRequest page, CancellationToken cancellationToken)
    {
        var tasks = _db.WorkTasks.AsNoTracking();
        if (query.Status is not null)
        {
            tasks = tasks.Where(t => t.Status == query.Status);
        }

        if (query.AssigneeId is not null)
        {
            tasks = tasks.Where(t => t.Assignments.Any(a => a.EmployeeId == query.AssigneeId));
        }

        var result = await tasks
            .OrderByDescending(t => t.Id)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ModuleId,
                ModuleName = t.Module.Name,
                t.Module.ProjectId,
                ProjectName = t.Module.Project.Name,
                t.Status,
                t.CommittedDate,
                t.ActualCompletionDate,
                t.ReopenCount,
                Assignees = t.Assignments.Select(a => new AssigneeDto(a.EmployeeId, a.Employee.Name, a.IsOriginalDeliverer)).ToList(),
            })
            .ToPagedResultAsync(page, cancellationToken);

        return new PagedResult<TaskSummaryDto>(
            result.Items.Select(t => new TaskSummaryDto(
                t.Id, t.Title, t.ModuleId, t.ModuleName, t.ProjectId, t.ProjectName, t.Status, t.CommittedDate, t.ActualCompletionDate, t.ReopenCount, t.Assignees)).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<TaskDetailDto> GetAsync(int taskId, CancellationToken cancellationToken)
    {
        var task = await _db.WorkTasks.AsNoTracking()
            .Include(t => t.Module).ThenInclude(m => m.Project)
            .Include(t => t.Assignments).ThenInclude(a => a.Employee)
            .Include(t => t.History)
            .Include(t => t.Bugs)
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("Task", taskId);

        var actorIds = task.History.Where(h => h.ChangedByEmployeeId != null).Select(h => h.ChangedByEmployeeId!.Value).Distinct().ToList();
        var names = await _db.Employees.AsNoTracking()
            .Where(e => actorIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);

        var logs = await _db.TimeLogs.AsNoTracking()
            .Where(l => l.TaskId == taskId)
            .OrderBy(l => l.WorkDate).ThenBy(l => l.Id)
            .Select(l => new TaskTimeLogDto(l.Id, l.EmployeeId, l.Employee.Name, l.WorkDate, l.Hours, l.WorkType, l.BugId, l.ApprovalStatus, l.Note))
            .ToListAsync(cancellationToken);

        var warrantyDays = task.Module.Project.WarrantyDays;
        var qaMissDeductible = (await _policies.GetSnapshotAsync(cancellationToken)).QaMissDeductible;
        return new TaskDetailDto(
            ToSummary(task),
            warrantyDays,
            task.ActualCompletionDate is { } completed ? WarrantyPolicy.LastWarrantyDay(completed, warrantyDays) : null,
            task.Version,
            AllowedTransitions(task.Status),
            task.History
                .OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.Id)
                .Select(h => new TaskHistoryDto(h.Id, h.FromStatus, h.ToStatus, h.ChangedByEmployeeId, h.ChangedByEmployeeId is null ? "System" : names.GetValueOrDefault(h.ChangedByEmployeeId.Value, "Unknown"), h.ChangedAtUtc, h.Reason))
                .ToList(),
            task.Bugs
                .OrderBy(b => b.Id)
                .Select(b => new TaskBugDto(b.Id, b.Severity, b.Description, b.FoundDate, b.Status, b.WithinWarranty, b.RootCause, b.CausedByOriginalDelivery, b.IsReviewed, b.IsDeductible(qaMissDeductible)))
                .ToList(),
            logs,
            task.ChangeRequestId);
    }

    public async Task<TaskSummaryDto> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var module = await _db.Modules.Include(m => m.Project).SingleOrDefaultAsync(m => m.Id == request.ModuleId, cancellationToken)
            ?? throw new NotFoundException("Module", request.ModuleId);

        var activeAssignees = await _db.Employees
            .Where(e => request.AssigneeIds.Contains(e.Id) && e.IsActive)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        var missing = request.AssigneeIds.Except(activeAssignees).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Employee", string.Join(", ", missing));
        }

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var task = new WorkTask(module, request.Title.Trim(), request.CommittedDate, request.AssigneeIds);
        _db.WorkTasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AuditEntities.Task, task.Id.ToString(CultureInfo.InvariantCulture), "Created", new
        {
            task.Title,
            task.ModuleId,
            task.CommittedDate,
            AssigneeIds = request.AssigneeIds,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await SummaryAsync(task.Id, cancellationToken);
    }

    /// <summary>
    /// Applies a user-requested status change. Assignees move their own tasks; team leads and managers can move any task.
    /// Closing requires a team lead or manager, and only a manager can close inside the warranty window.
    /// </summary>
    public async Task<TaskDetailDto> TransitionAsync(int taskId, TransitionTaskRequest request, CancellationToken cancellationToken)
    {
        await _transitionValidator.ValidateAndThrowAsync(request, cancellationToken);

        var task = await _db.LoadAggregateAsync(taskId, cancellationToken);
        var isLeadOrManager = _user.Role is Role.TeamLead or Role.Manager;
        var historyBefore = task.History.Count;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        if (request.TargetStatus == WorkTaskStatus.Closed)
        {
            if (!isLeadOrManager)
            {
                throw new ForbiddenException("Only a team lead or manager can close a task.");
            }

            task.Close(_user.EmployeeId, _user.Role == Role.Manager, task.Module.Project.WarrantyDays, reason, _clock.UtcNow);
        }
        else
        {
            if (!isLeadOrManager && !task.IsAssignedTo(_user.EmployeeId))
            {
                throw new ForbiddenException("You can only change the status of tasks assigned to you.");
            }

            task.MoveTo(request.TargetStatus, _user.EmployeeId, reason, _clock.UtcNow);
        }

        TaskAudit.RecordTransitions(_audit, task, historyBefore);
        if (task.ChangeRequestId is int changeRequestId)
        {
            // A feature request needs no separate start or complete step: it follows its task.
            var changeRequest = await _db.ChangeRequests.SingleAsync(c => c.Id == changeRequestId, cancellationToken);
            var from = changeRequest.Status;
            foreach (var action in changeRequest.FollowTask(task.Status))
            {
                _audit.Record(AuditEntities.ChangeRequest, changeRequestId.ToString(CultureInfo.InvariantCulture), action, new { From = from, To = changeRequest.Status, TaskId = task.Id, Automatic = true });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(taskId, cancellationToken);
    }

    private static IReadOnlyList<WorkTaskStatus> AllowedTransitions(WorkTaskStatus status) => TaskStateMachine.ManualTargets(status);

    private async Task<TaskSummaryDto> SummaryAsync(int taskId, CancellationToken cancellationToken) =>
        (await GetAsync(taskId, cancellationToken)).Task;

    private static TaskSummaryDto ToSummary(WorkTask t) => new(
        t.Id,
        t.Title,
        t.ModuleId,
        t.Module.Name,
        t.Module.ProjectId,
        t.Module.Project.Name,
        t.Status,
        t.CommittedDate,
        t.ActualCompletionDate,
        t.ReopenCount,
        t.Assignments.Select(a => new AssigneeDto(a.EmployeeId, a.Employee.Name, a.IsOriginalDeliverer)).ToList());
}
