using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Deductions;

/// <summary>
/// The deduction lifecycle after drafting: notify the employee, dispute and resolve, final approval, cancellation,
/// acknowledgement and compensation of finalized lines. Every step is audited.
/// </summary>
public sealed class DeductionService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IWorkCalendar _calendar;
    private readonly PolicyProvider _policies;
    private readonly DeductionLedger _ledger;
    private readonly NotificationPublisher _notifications;
    private readonly PayrollCalendar _payrollCalendar;
    private readonly IValidator<DeductionIdsRequest> _idsValidator;
    private readonly IValidator<RaiseDisputeRequest> _disputeValidator;
    private readonly IValidator<ResolveDisputeRequest> _resolveValidator;
    private readonly IValidator<ReasonRequest> _reasonValidator;
    private readonly DeductionOptions _options;

    public DeductionService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        IWorkCalendar calendar,
        PolicyProvider policies,
        DeductionLedger ledger,
        NotificationPublisher notifications,
        PayrollCalendar payrollCalendar,
        IValidator<DeductionIdsRequest> idsValidator,
        IValidator<RaiseDisputeRequest> disputeValidator,
        IValidator<ResolveDisputeRequest> resolveValidator,
        IValidator<ReasonRequest> reasonValidator,
        IOptions<DeductionOptions>? options = null)
    {
        _options = options?.Value ?? new DeductionOptions();
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _calendar = calendar;
        _policies = policies;
        _ledger = ledger;
        _notifications = notifications;
        _payrollCalendar = payrollCalendar;
        _idsValidator = idsValidator;
        _disputeValidator = disputeValidator;
        _resolveValidator = resolveValidator;
        _reasonValidator = reasonValidator;
    }

    public async Task<PagedResult<DeductionDto>> ListAsync(DeductionListQuery query, PageRequest page, CancellationToken cancellationToken)
    {
        var visible = EmployeeScope.VisibleEmployeeIds(_db, _user);
        var lines = _db.Deductions.AsNoTracking().Where(d => visible.Contains(d.EmployeeId));
        if (query.EmployeeId is not null)
        {
            lines = lines.Where(d => d.EmployeeId == query.EmployeeId);
        }

        if (query.Status is not null)
        {
            lines = lines.Where(d => d.Status == query.Status);
        }

        if (query.ApplyMonth is not null)
        {
            if (!YearMonth.TryParse(query.ApplyMonth, out var month))
            {
                throw new ValidationException([new FluentValidation.Results.ValidationFailure("applyMonth", "applyMonth must be yyyy-MM.")]);
            }

            var text = month.ToString();
            lines = lines.Where(d => d.ApplyMonth == text);
        }

        if (query.BugId is not null)
        {
            lines = lines.Where(d => d.BugId == query.BugId);
        }

        if (query.OpenDispute == true)
        {
            lines = lines.Where(d => d.Status == DeductionStatus.Disputed);
        }

        var now = _clock.UtcNow;
        return await Project(lines.OrderByDescending(d => d.Id), now).ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<DeductionDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var line = await Project(_db.Deductions.AsNoTracking().Where(d => d.Id == id), _clock.UtcNow).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Deduction", id);
        await EmployeeScope.EnsureVisibleAsync(_db, _user, line.EmployeeId, cancellationToken);
        return line;
    }

    /// <summary>Tells each employee about their Draft lines and opens the dispute window (working days, IST).</summary>
    public async Task<IReadOnlyList<DeductionDto>> NotifyAsync(DeductionIdsRequest request, CancellationToken cancellationToken)
    {
        await _idsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var policy = await _policies.GetSnapshotAsync(cancellationToken);
        var now = _clock.UtcNow;
        var windowEnd = await _calendar.WindowEndUtcAsync(now, policy.DisputeWindowDays, cancellationToken);

        return await ForEachAsync(request.DeductionIds, "Notified", line =>
        {
            line.Notify(now, windowEnd);
            return new { line.EmployeeId, line.Amount, WindowEndsAtUtc = windowEnd, policy.DisputeWindowDays };
        }, lines => lines.GroupBy(l => l.EmployeeId).Select(g =>
            NotificationTemplates.DeductionsNotified(g.Key, g.Count(), g.Sum(l => l.Amount), windowEnd, policy.DisputeWindowDays)),
        cancellationToken);
    }

    /// <summary>Manager's final approval. Each line must be past its window with no open dispute, or resolved.</summary>
    public async Task<IReadOnlyList<DeductionDto>> ApproveAsync(DeductionIdsRequest request, CancellationToken cancellationToken)
    {
        await _idsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var now = _clock.UtcNow;
        return await ForEachAsync(request.DeductionIds, "Approved", line =>
        {
            line.Approve(_user.EmployeeId, now, _options.EnforceDisputeWindow);
            return new { line.EmployeeId, line.Amount };
        }, lines => lines.GroupBy(l => l.EmployeeId).Select(g => NotificationTemplates.DeductionsApproved(g.Key, g.Count(), g.Sum(l => l.Amount))),
        cancellationToken);
    }

    public async Task<DeductionDto> RaiseDisputeAsync(int id, RaiseDisputeRequest request, CancellationToken cancellationToken)
    {
        await _disputeValidator.ValidateAndThrowAsync(request, cancellationToken);
        var line = await LoadAsync(id, cancellationToken);
        if (line.EmployeeId != _user.EmployeeId)
        {
            // Hide other people's lines entirely rather than confirming they exist.
            await EmployeeScope.EnsureVisibleAsync(_db, _user, line.EmployeeId, cancellationToken);
        }

        var dispute = line.RaiseDispute(_user.EmployeeId, request.Reason, _clock.UtcNow);
        Record(line, "DisputeRaised", new { dispute.Reason, line.Amount });
        var employeeName = await _db.Employees.Where(e => e.Id == line.EmployeeId).Select(e => e.Name).SingleAsync(cancellationToken);
        var managers = await _notifications.ActiveEmployeesInRoleAsync(Role.Manager, cancellationToken);
        await _notifications.PublishAsync(
            managers.Where(m => m != line.EmployeeId).Select(m => NotificationTemplates.DisputeRaised(m, line.Id, employeeName, line.Amount)).ToList(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<DeductionDto> ResolveDisputeAsync(int id, ResolveDisputeRequest request, CancellationToken cancellationToken)
    {
        await _resolveValidator.ValidateAndThrowAsync(request, cancellationToken);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var line = await LoadAsync(id, cancellationToken);
        var before = new { line.Hours, line.SharePercent, line.Amount };

        var replacement = line.ResolveDispute(request.Outcome, request.Justification, request.NewHours, request.NewSharePercent, _user.EmployeeId, _clock.UtcNow);
        if (replacement is not null)
        {
            _db.Deductions.Add(replacement);
            await _db.SaveChangesAsync(cancellationToken);
            line.Dispute!.LinkReplacement(replacement.Id);
        }

        Record(line, "DisputeResolved", new
        {
            request.Outcome,
            Justification = line.Dispute!.ResolutionJustification,
            Before = before,
            ReplacementDeductionId = replacement?.Id,
            ReplacementAmount = replacement?.Amount,
            ResultStatus = line.Status,
        });
        await _notifications.PublishAsync(
            NotificationTemplates.DisputeResolved(line.EmployeeId, replacement?.Id ?? line.Id, request.Outcome, before.Amount, replacement?.Amount, line.Dispute.ResolutionJustification!),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(replacement?.Id ?? id, cancellationToken);
    }

    public async Task<DeductionDto> CancelAsync(int id, ReasonRequest request, CancellationToken cancellationToken)
    {
        await _reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var line = await LoadAsync(id, cancellationToken);
        line.CancelByDecision(request.Reason, _clock.UtcNow);
        Record(line, "Cancelled", new { line.CancellationReason, line.Amount });
        if (line.NotifiedAtUtc is not null)
        {
            await _notifications.PublishAsync(NotificationTemplates.DeductionCancelled(line.EmployeeId, line.Id, line.Amount, line.CancellationReason!), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<DeductionDto> AcknowledgeAsync(int id, CancellationToken cancellationToken)
    {
        var line = await LoadAsync(id, cancellationToken);
        await EmployeeScope.EnsureVisibleAsync(_db, _user, line.EmployeeId, cancellationToken);
        line.Acknowledge(_user.EmployeeId, _clock.UtcNow);
        Record(line, "Acknowledged", new { line.Amount });
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Raises a compensating (negative) line against an applied line; it goes into the next payroll run.</summary>
    public async Task<DeductionDto> CompensateAsync(int id, ReasonRequest request, CancellationToken cancellationToken)
    {
        await _reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var line = await LoadAsync(id, cancellationToken);
        if (await _db.Deductions.AnyAsync(d => d.ReversesDeductionId == id, cancellationToken))
        {
            throw new DomainException("deduction.already_compensated", "This line already has a compensating line.");
        }

        var compensation = Deduction.Compensate(line, await _payrollCalendar.CompensationMonthForAsync(line, cancellationToken), request.Reason, _user.EmployeeId, _clock.UtcNow);
        _db.Deductions.Add(compensation);
        await _db.SaveChangesAsync(cancellationToken);
        Record(compensation, "CompensationRaised", new { compensation.ReversesDeductionId, compensation.Amount, compensation.Note });
        await _notifications.PublishAsync(NotificationTemplates.CompensationRaised(compensation.EmployeeId, compensation.Id, compensation.Amount, compensation.ApplyMonth), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(compensation.Id, cancellationToken);
    }

    /// <summary>Re-runs the ledger for a bug, e.g. after a pay rate correction. Idempotent.</summary>
    public async Task<IReadOnlyList<DeductionDto>> RecalculateBugAsync(int bugId, CancellationToken cancellationToken)
    {
        if (!await _db.Bugs.AnyAsync(b => b.Id == bugId, cancellationToken))
        {
            throw new NotFoundException("Bug", bugId);
        }

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _ledger.SyncBugsAsync([bugId], "manual recalculation", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await Project(_db.Deductions.AsNoTracking().Where(d => d.BugId == bugId).OrderBy(d => d.Id), _clock.UtcNow).ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DeductionDto>> ForEachAsync(
        IReadOnlyList<int> ids,
        string action,
        Func<Deduction, object> change,
        Func<IReadOnlyList<Deduction>, IEnumerable<NotificationRequest>> notify,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var lines = await _db.Deductions.Include(d => d.Dispute).Where(d => ids.Contains(d.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(lines.Select(l => l.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Deduction", string.Join(", ", missing));
        }

        foreach (var line in lines.OrderBy(l => l.Id))
        {
            try
            {
                Record(line, action, change(line));
            }
            catch (DomainException ex)
            {
                throw new DomainException(ex.Code, $"Line {line.Id}: {ex.Message}");
            }
        }

        await _notifications.PublishAsync(notify(lines).ToList(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var now = _clock.UtcNow;
        return await Project(_db.Deductions.AsNoTracking().Where(d => ids.Contains(d.Id)).OrderBy(d => d.Id), now).ToListAsync(cancellationToken);
    }

    private async Task<Deduction> LoadAsync(int id, CancellationToken cancellationToken) =>
        await _db.Deductions.Include(d => d.Dispute).SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
        ?? throw new NotFoundException("Deduction", id);

    private void Record(Deduction line, string action, object details) =>
        _audit.Record(AuditEntities.Deduction, line.Id.ToString(CultureInfo.InvariantCulture), action, details);

    internal IQueryable<DeductionDto> Project(IQueryable<Deduction> lines, DateTime now) =>
        from d in lines
        join e in _db.Employees on d.EmployeeId equals e.Id
        join b in _db.Bugs on d.BugId equals b.Id
        select new DeductionDto(
            d.Id,
            d.Kind,
            d.Status,
            d.EmployeeId,
            e.Name,
            d.BugId,
            b.Description.Length > 200 ? b.Description.Substring(0, 200) : b.Description,
            b.TaskId,
            b.Task.Title,
            b.Task.ModuleId,
            b.Task.Module.Name,
            d.TimeLogId,
            d.WorkDate,
            d.SourceMonth,
            d.ApplyMonth,
            d.RoutedFromLockedMonth,
            d.Hours,
            d.HourlyRateUsed,
            d.SharePercent,
            d.Amount,
            d.ReversesDeductionId,
            d.AmountSetByDispute,
            d.CreatedAtUtc,
            d.NotifiedAtUtc,
            d.DisputeWindowEndsAtUtc,
            d.DisputeWindowEndsAtUtc != null && d.DisputeWindowEndsAtUtc >= now,
            d.AcknowledgedAtUtc,
            d.ApprovedByEmployeeId,
            d.ApprovedAtUtc,
            d.PayrollRunId,
            d.CancellationReason,
            d.Note,
            d.Dispute == null
                ? null
                : new DeductionDisputeDto(
                    d.Dispute.Id,
                    d.Dispute.Reason,
                    d.Dispute.RaisedAtUtc,
                    d.Dispute.Outcome,
                    d.Dispute.ResolutionJustification,
                    d.Dispute.ResolvedByEmployeeId,
                    d.Dispute.ResolvedAtUtc,
                    d.Dispute.ReplacementDeductionId),
            d.Version,
            d.Status == DeductionStatus.Resolved
                || (d.Status == DeductionStatus.Notified && d.Dispute == null
                    && (!_options.EnforceDisputeWindow || d.DisputeWindowEndsAtUtc == null || d.DisputeWindowEndsAtUtc < now)));
}
