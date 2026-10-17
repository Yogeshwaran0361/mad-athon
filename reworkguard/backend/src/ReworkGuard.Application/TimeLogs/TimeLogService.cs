using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.TimeLogs;

public sealed class TimeLogService
{
    private static readonly Role[] RolesSeeingEveryone = [Role.TeamLead, Role.Manager, Role.Finance, Role.Admin];

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<LogTimeRequest> _logValidator;
    private readonly IValidator<RejectTimeLogRequest> _rejectValidator;
    private readonly DeductionLedger _ledger;
    private readonly NotificationPublisher _notifications;

    public TimeLogService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IValidator<LogTimeRequest> logValidator,
        IValidator<RejectTimeLogRequest> rejectValidator,
        DeductionLedger ledger,
        NotificationPublisher notifications)
    {
        _ledger = ledger;
        _notifications = notifications;
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _logValidator = logValidator;
        _rejectValidator = rejectValidator;
    }

    /// <summary>
    /// Logs the current user's hours. The employee row is locked for the transaction so concurrent requests
    /// cannot together exceed the 24-hour daily limit.
    /// </summary>
    public async Task<TimeLogDto> LogAsync(LogTimeRequest request, CancellationToken cancellationToken)
    {
        await _logValidator.ValidateAndThrowAsync(request, cancellationToken);
        var employeeId = _user.EmployeeId;

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _db.LockEmployeeAsync(employeeId, cancellationToken);

        var task = await _db.LoadAggregateAsync(request.TaskId, cancellationToken);
        var historyBefore = task.History.Count;
        var alreadyLogged = await _db.TimeLogs
            .Where(l => l.EmployeeId == employeeId && l.WorkDate == request.WorkDate && l.ApprovalStatus != ApprovalStatus.Rejected)
            .SumAsync(l => (decimal?)l.Hours, cancellationToken) ?? 0m;

        var log = request.WorkType switch
        {
            WorkType.Corrective => task.LogCorrectiveHours(request.BugId!.Value, employeeId, request.WorkDate, request.Hours, request.Note, alreadyLogged, _clock.UtcNow),
            WorkType.Enhancement => task.LogEnhancementHours(
                await ChangeRequestOfAsync(task, cancellationToken), employeeId, request.WorkDate, request.Hours, request.Note, alreadyLogged, _clock.UtcNow),
            _ => task.LogRegularHours(employeeId, request.WorkDate, request.Hours, request.Note, alreadyLogged, _clock.UtcNow),
        };

        _db.TimeLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AuditEntities.TimeLog, log.Id.ToString(CultureInfo.InvariantCulture), "Logged", new
        {
            log.TaskId,
            log.EmployeeId,
            log.WorkDate,
            log.Hours,
            log.WorkType,
            log.BugId,
        });
        TaskAudit.RecordTransitions(_audit, task, historyBefore);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(log.Id, cancellationToken);
    }

    public async Task<PagedResult<TimeLogDto>> ListAsync(TimeLogListQuery query, PageRequest page, CancellationToken cancellationToken)
    {
        var employeeId = RolesSeeingEveryone.Contains(_user.Role) ? query.EmployeeId : _user.EmployeeId;
        var logs = _db.TimeLogs.AsNoTracking();
        if (employeeId is not null)
        {
            logs = logs.Where(l => l.EmployeeId == employeeId);
        }

        if (query.TaskId is not null)
        {
            logs = logs.Where(l => l.TaskId == query.TaskId);
        }

        if (query.ApprovalStatus is not null)
        {
            logs = logs.Where(l => l.ApprovalStatus == query.ApprovalStatus);
        }

        if (query.WorkType is not null)
        {
            logs = logs.Where(l => l.WorkType == query.WorkType);
        }

        return await Project(logs.OrderByDescending(l => l.WorkDate).ThenByDescending(l => l.Id)).ToPagedResultAsync(page, cancellationToken);
    }

    /// <summary>Pending logs the current approver may decide on: everyone's except their own, oldest first.</summary>
    public Task<PagedResult<TimeLogDto>> ApprovalQueueAsync(WorkType? workType, PageRequest page, CancellationToken cancellationToken)
    {
        var approverId = _user.EmployeeId;
        var logs = _db.TimeLogs.AsNoTracking()
            .Where(l => l.ApprovalStatus == ApprovalStatus.Pending && l.EmployeeId != approverId);
        if (workType is not null)
        {
            logs = logs.Where(l => l.WorkType == workType);
        }

        return Project(logs.OrderBy(l => l.WorkDate).ThenBy(l => l.Id)).ToPagedResultAsync(page, cancellationToken);
    }

    public Task<TimeLogDto> ApproveAsync(int timeLogId, CancellationToken cancellationToken) =>
        DecideAsync(timeLogId, "Approved", null, log => log.Approve(_user.EmployeeId, _clock.UtcNow), cancellationToken);

    public async Task<TimeLogDto> RejectAsync(int timeLogId, RejectTimeLogRequest request, CancellationToken cancellationToken)
    {
        await _rejectValidator.ValidateAndThrowAsync(request, cancellationToken);
        return await DecideAsync(timeLogId, "Rejected", request.Reason?.Trim(), log => log.Reject(_user.EmployeeId, _clock.UtcNow), cancellationToken);
    }

    /// <summary>
    /// Records the decision. An approval can change deductions (corrective hours on a deductible bug, or regular hours
    /// that move the attribution split), so the ledger is re-synced in the same transaction.
    /// </summary>
    private async Task<TimeLogDto> DecideAsync(int timeLogId, string action, string? reason, Action<TimeLog> decide, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var log = await _db.TimeLogs.SingleOrDefaultAsync(l => l.Id == timeLogId, cancellationToken)
            ?? throw new NotFoundException("Time log", timeLogId);

        decide(log);
        _audit.Record(AuditEntities.TimeLog, log.Id.ToString(CultureInfo.InvariantCulture), action, new
        {
            log.EmployeeId,
            log.TaskId,
            log.WorkType,
            log.BugId,
            log.Hours,
            log.WorkDate,
            Reason = reason,
        });
        if (log.ApprovalStatus == ApprovalStatus.Rejected)
        {
            await _notifications.PublishAsync(NotificationTemplates.TimeLogRejected(log.EmployeeId, log.Id, log.TaskId, log.WorkDate, log.Hours, reason), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Enhancement hours are paid change-request work: they never touch the deduction ledger.
        if (log.ApprovalStatus == ApprovalStatus.Approved && log.WorkType != WorkType.Enhancement)
        {
            var trigger = $"time log {log.Id} approval";
            await (log.WorkType == WorkType.Corrective
                ? _ledger.SyncBugsAsync([log.BugId!.Value], trigger, cancellationToken)
                : _ledger.SyncTaskAsync(log.TaskId, trigger, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(log.Id, cancellationToken);
    }

    private async Task<ChangeRequest> ChangeRequestOfAsync(WorkTask task, CancellationToken cancellationToken) =>
        task.ChangeRequestId is { } id
            ? await _db.ChangeRequests.SingleAsync(c => c.Id == id, cancellationToken)
            : throw new Domain.Common.DomainException("timelog.enhancement_not_allowed", "Enhancement hours can only be logged on a change request task.");

    private async Task<TimeLogDto> GetAsync(int timeLogId, CancellationToken cancellationToken) =>
        await Project(_db.TimeLogs.AsNoTracking().Where(l => l.Id == timeLogId)).SingleAsync(cancellationToken);

    private static IQueryable<TimeLogDto> Project(IQueryable<TimeLog> logs) =>
        logs.Select(l => new TimeLogDto(
            l.Id,
            l.TaskId,
            l.Task.Title,
            l.Task.Status,
            l.EmployeeId,
            l.Employee.Name,
            l.WorkDate,
            l.Hours,
            l.WorkType,
            l.BugId,
            l.ApprovalStatus,
            l.ApprovedByEmployeeId,
            l.ApprovedAtUtc,
            l.Note,
            l.Version));
}
