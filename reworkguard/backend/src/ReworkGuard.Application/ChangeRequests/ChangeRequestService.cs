using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.ChangeRequests;

/// <summary>
/// Customer enhancement requests on delivered projects. They are new paid work: the original task is never reopened,
/// no bug is raised and nothing enters the deduction flow. Every transition is audited.
/// </summary>
public sealed class ChangeRequestService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<CreateChangeRequestRequest> _createValidator;
    private readonly IValidator<ApproveChangeRequestRequest> _approveValidator;
    private readonly IValidator<RejectChangeRequestRequest> _rejectValidator;
    private readonly IValidator<CloseChangeRequestRequest> _closeValidator;
    private readonly IChangeRequestClosedHandler? _onClosed;

    public ChangeRequestService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IValidator<CreateChangeRequestRequest> createValidator,
        IValidator<ApproveChangeRequestRequest> approveValidator,
        IValidator<RejectChangeRequestRequest> rejectValidator,
        IValidator<CloseChangeRequestRequest> closeValidator,
        IEnumerable<IChangeRequestClosedHandler> onClosed)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _createValidator = createValidator;
        _approveValidator = approveValidator;
        _rejectValidator = rejectValidator;
        _closeValidator = closeValidator;
        _onClosed = onClosed.SingleOrDefault();
    }

    /// <summary>Works for any project, however old, and whatever state its tasks are in.</summary>
    public async Task<ChangeRequestDto> CreateAsync(CreateChangeRequestRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var module = await _db.Modules.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.ModuleId, cancellationToken)
            ?? throw new NotFoundException("Module", request.ModuleId);
        if (module.ProjectId != request.ProjectId)
        {
            throw new DomainException("cr.module_not_in_project", "The module does not belong to that project.");
        }

        var cr = new ChangeRequest(request.ProjectId, request.ModuleId, request.Title, request.CustomerName, request.ClientReference, request.QuotedAmountExTax, _clock.UtcNow);
        _db.ChangeRequests.Add(cr);
        await _db.SaveChangesAsync(cancellationToken);
        Record(cr, "Requested", new { cr.ProjectId, cr.ModuleId, cr.Title, cr.CustomerName, cr.ClientReference, cr.QuotedAmountExTax });
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cr.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ChangeRequestDto>> ListAsync(ChangeRequestStatus? status, CancellationToken cancellationToken)
    {
        var query = _db.ChangeRequests.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        return await Project(query.OrderByDescending(c => c.Id)).ToListAsync(cancellationToken);
    }

    public async Task<ChangeRequestDto> GetAsync(int id, CancellationToken cancellationToken) =>
        await Project(_db.ChangeRequests.AsNoTracking().Where(c => c.Id == id)).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Change request", id);

    /// <summary>Approves the request and creates one new task, linked to it, for the named assignees.</summary>
    public async Task<ChangeRequestDto> ApproveAsync(int id, ApproveChangeRequestRequest request, CancellationToken cancellationToken)
    {
        await _approveValidator.ValidateAndThrowAsync(request, cancellationToken);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var cr = await LoadAsync(id, cancellationToken);
        var from = cr.Status;
        cr.Approve(_user.EmployeeId, request.ApprovedHours);

        var known = await _db.Employees.CountAsync(e => request.AssigneeIds.Contains(e.Id) && e.IsActive, cancellationToken);
        if (known != request.AssigneeIds.Count)
        {
            throw new DomainException("cr.unknown_assignee", "Every assignee must be an active employee.");
        }

        var module = await _db.Modules.SingleAsync(m => m.Id == cr.ModuleId, cancellationToken);
        var committed = request.CommittedDate ?? WorkTimeZone.ToWorkDate(_clock.UtcNow).AddDays(14);
        var task = WorkTask.ForChangeRequest(module, cr.Id, $"CR {cr.ClientReference}: {cr.Title}", committed, request.AssigneeIds);
        _db.WorkTasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);

        Record(cr, "Approved", new { From = from, To = cr.Status, cr.ApprovedHours, TaskId = task.Id, request.AssigneeIds });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<ChangeRequestDto> RejectAsync(int id, RejectChangeRequestRequest request, CancellationToken cancellationToken)
    {
        await _rejectValidator.ValidateAndThrowAsync(request, cancellationToken);
        return await TransitionAsync(id, "Rejected", cr => cr.Reject(request.Reason), new { request.Reason }, cancellationToken);
    }

    public Task<ChangeRequestDto> StartAsync(int id, CancellationToken cancellationToken) =>
        TransitionAsync(id, "Started", cr => cr.Start(), null, cancellationToken);

    public Task<ChangeRequestDto> CompleteAsync(int id, CancellationToken cancellationToken) =>
        TransitionAsync(id, "Completed", cr => cr.Complete(), null, cancellationToken);

    /// <summary>Finance closes the request with the amount actually received, which settles any profit share.</summary>
    public async Task<ChangeRequestDto> CloseAsync(int id, CloseChangeRequestRequest request, CancellationToken cancellationToken)
    {
        await _closeValidator.ValidateAndThrowAsync(request, cancellationToken);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var cr = await LoadAsync(id, cancellationToken);
        var from = cr.Status;
        cr.Close(request.ReceivedAmountExTax);
        Record(cr, "Closed", new { From = from, To = cr.Status, cr.ReceivedAmountExTax });
        await _db.SaveChangesAsync(cancellationToken);
        if (_onClosed is not null)
        {
            await _onClosed.OnClosedAsync(cr, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<ChangeRequestDto> TransitionAsync(int id, string action, Action<ChangeRequest> change, object? details, CancellationToken cancellationToken)
    {
        var cr = await LoadAsync(id, cancellationToken);
        var from = cr.Status;
        change(cr);
        Record(cr, action, new { From = from, To = cr.Status, Details = details });
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<ChangeRequest> LoadAsync(int id, CancellationToken cancellationToken) =>
        await _db.ChangeRequests.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Change request", id);

    private void Record(ChangeRequest cr, string action, object details) =>
        _audit.Record(AuditEntities.ChangeRequest, cr.Id.ToString(CultureInfo.InvariantCulture), action, details);

    private IQueryable<ChangeRequestDto> Project(IQueryable<ChangeRequest> query)
    {
        var seesMoney = EmployeeScope.SeesEveryone(_user.Role);
        return from c in query
               join m in _db.Modules on c.ModuleId equals m.Id
               select new ChangeRequestDto(
                   c.Id,
                   c.ProjectId,
                   m.Project.Name,
                   c.ModuleId,
                   m.Name,
                   c.Title,
                   c.CustomerName,
                   c.ClientReference,
                   seesMoney ? c.QuotedAmountExTax : null,
                   c.ApprovedHours,
                   seesMoney ? c.ReceivedAmountExTax : null,
                   c.Status,
                   c.CreatedAtUtc,
                   c.ApprovedByEmployeeId,
                   c.RejectionReason,
                   _db.WorkTasks.Where(t => t.ChangeRequestId == c.Id).Select(t => (int?)t.Id).FirstOrDefault(),
                   c.Version);
    }
}

/// <summary>Runs inside the close transaction once a change request is Closed (the profit-share settlement).</summary>
public interface IChangeRequestClosedHandler
{
    Task OnClosedAsync(ChangeRequest changeRequest, CancellationToken cancellationToken);
}
