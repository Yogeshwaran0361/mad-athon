using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.ChangeRequests;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.ProfitShare;

/// <summary>
/// Settles a closed change request's profit share into one Pending award per contributor, lets a manager decide
/// them, and lists them. Employees only ever see their own award amounts; revenue, profit and rates stay with
/// Manager, Finance and Admin.
/// </summary>
public sealed class ProfitShareService : IChangeRequestClosedHandler
{
    public const decimal DefaultProfitSharePercent = 20m;
    public const decimal DefaultLabourOverheadFactor = 1.5m;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly HourlyRateResolver _rates;
    private readonly PolicyProvider _policies;
    private readonly PayrollCalendar _calendar;
    private readonly IValidator<RejectAwardRequest> _rejectValidator;

    public ProfitShareService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        HourlyRateResolver rates,
        PolicyProvider policies,
        PayrollCalendar calendar,
        IValidator<RejectAwardRequest> rejectValidator)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _rates = rates;
        _policies = policies;
        _calendar = calendar;
        _rejectValidator = rejectValidator;
    }

    /// <summary>
    /// Computes the pool from approved Enhancement hours and creates the awards. Idempotent: a request that is already
    /// settled, or already has awards, is left alone.
    /// </summary>
    public async Task OnClosedAsync(ChangeRequest changeRequest, CancellationToken cancellationToken)
    {
        if (changeRequest.ProfitShareSettledAtUtc is not null || await _db.IncentiveAwards.AnyAsync(a => a.ChangeRequestId == changeRequest.Id, cancellationToken))
        {
            return;
        }

        var settings = await _db.PolicySettings.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        var percent = Read(settings, PolicySetting.ProfitSharePercent, DefaultProfitSharePercent);
        var overhead = Read(settings, PolicySetting.LabourOverheadFactor, DefaultLabourOverheadFactor);
        var deduction = await _policies.GetDeductionPolicyAsync(cancellationToken);

        var logs = await _db.TimeLogs.AsNoTracking()
            .Where(l => l.WorkType == WorkType.Enhancement && l.ApprovalStatus == ApprovalStatus.Approved && l.Task.ChangeRequestId == changeRequest.Id)
            .Select(l => new { l.EmployeeId, l.WorkDate, l.Hours })
            .ToListAsync(cancellationToken);
        await _rates.PreloadAsync(logs.Select(l => l.EmployeeId).Distinct().ToList(), cancellationToken);
        var labour = new List<ProfitShareLabour>(logs.Count);
        foreach (var log in logs)
        {
            labour.Add(new ProfitShareLabour(log.EmployeeId, log.Hours, await _rates.HourlyRateAsync(log.EmployeeId, log.WorkDate, deduction, cancellationToken)));
        }

        var now = _clock.UtcNow;
        var result = ProfitShareCalculator.Calculate(changeRequest.ReceivedAmountExTax, overhead, percent, labour);
        changeRequest.RecordProfitShare(result.LabourCost, result.Profit, result.Pool, now);
        var applyMonth = await _calendar.ApplyMonthForAsync(YearMonth.From(WorkTimeZone.ToWorkDate(now)), cancellationToken);
        var awards = result.Lines.Select(l => new IncentiveAward(changeRequest.Id, l.EmployeeId, l.Hours, l.SharePercent, l.Amount, applyMonth, now)).ToList();
        _db.IncentiveAwards.AddRange(awards);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AuditEntities.ChangeRequest, changeRequest.Id.ToString(CultureInfo.InvariantCulture), "ProfitShareSettled", new
        {
            changeRequest.ReceivedAmountExTax,
            result.LabourCost,
            result.Profit,
            result.Pool,
            ProfitSharePercent = percent,
            LabourOverheadFactor = overhead,
            Awards = awards.Select(a => new { a.Id, a.EmployeeId, a.Hours, a.SharePercent, a.Amount, a.ApplyMonth }),
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProfitShareSummaryDto> SummaryAsync(int changeRequestId, CancellationToken cancellationToken)
    {
        var cr = await _db.ChangeRequests.AsNoTracking().SingleOrDefaultAsync(c => c.Id == changeRequestId, cancellationToken)
            ?? throw new NotFoundException("Change request", changeRequestId);
        var awards = await Project(_db.IncentiveAwards.AsNoTracking().Where(a => a.ChangeRequestId == changeRequestId)).ToListAsync(cancellationToken);
        return new ProfitShareSummaryDto(cr.Id, cr.ReceivedAmountExTax, cr.LabourCostExTax, cr.ProfitExTax, cr.ProfitSharePool, cr.ProfitShareSettledAtUtc, awards);
    }

    /// <summary>Manager, Finance and Admin see every award; anyone else only their own.</summary>
    public async Task<IReadOnlyList<IncentiveAwardDto>> ListAsync(IncentiveAwardStatus? status, CancellationToken cancellationToken)
    {
        var awards = _db.IncentiveAwards.AsNoTracking();
        if (!EmployeeScope.SeesEveryone(_user.Role))
        {
            var me = _user.EmployeeId;
            awards = awards.Where(a => a.EmployeeId == me);
        }

        if (status is not null)
        {
            awards = awards.Where(a => a.Status == status);
        }

        return await Project(awards.OrderBy(a => a.Status).ThenByDescending(a => a.Id)).ToListAsync(cancellationToken);
    }

    public Task<IncentiveAwardDto> ApproveAsync(int id, CancellationToken cancellationToken) =>
        DecideAsync(id, "Approved", a => a.Approve(_user.EmployeeId, _clock.UtcNow), null, cancellationToken);

    public async Task<IncentiveAwardDto> RejectAsync(int id, RejectAwardRequest request, CancellationToken cancellationToken)
    {
        await _rejectValidator.ValidateAndThrowAsync(request, cancellationToken);
        return await DecideAsync(id, "Rejected", a => a.Reject(_user.EmployeeId, request.Reason, _clock.UtcNow), request.Reason, cancellationToken);
    }

    /// <summary>Earning lines paid by a run, for one employee's payslip.</summary>
    public async Task<IReadOnlyList<EarningLineDto>> PaidInRunAsync(int payrollRunId, int employeeId, CancellationToken cancellationToken) =>
        await Lines(_db.IncentiveAwards.AsNoTracking().Where(a => a.PayrollRunId == payrollRunId && a.EmployeeId == employeeId)).ToListAsync(cancellationToken);

    /// <summary>Earning lines the given awards make up (a live preview).</summary>
    public async Task<IReadOnlyList<EarningLineDto>> LinesAsync(IReadOnlyCollection<int> awardIds, CancellationToken cancellationToken) =>
        await Lines(_db.IncentiveAwards.AsNoTracking().Where(a => awardIds.Contains(a.Id))).ToListAsync(cancellationToken);

    private async Task<IncentiveAwardDto> DecideAsync(int id, string action, Action<IncentiveAward> decide, string? reason, CancellationToken cancellationToken)
    {
        var award = await _db.IncentiveAwards.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new NotFoundException("Incentive award", id);
        decide(award);
        _audit.Record(AuditEntities.IncentiveAward, award.Id.ToString(CultureInfo.InvariantCulture), action, new { award.ChangeRequestId, award.EmployeeId, award.Amount, Reason = reason });
        await _db.SaveChangesAsync(cancellationToken);
        return await Project(_db.IncentiveAwards.AsNoTracking().Where(a => a.Id == id)).SingleAsync(cancellationToken);
    }

    private IQueryable<IncentiveAwardDto> Project(IQueryable<IncentiveAward> awards) =>
        from a in awards
        join c in _db.ChangeRequests on a.ChangeRequestId equals c.Id
        join e in _db.Employees on a.EmployeeId equals e.Id
        select new IncentiveAwardDto(
            a.Id, a.ChangeRequestId, c.Title, a.EmployeeId, e.Name, a.Hours, a.SharePercent, a.Amount, a.Status, a.ApplyMonth, a.DecidedByEmployeeId, a.RejectionReason, a.PayrollRunId, a.Version);

    private IQueryable<EarningLineDto> Lines(IQueryable<IncentiveAward> awards) =>
        from a in awards
        join c in _db.ChangeRequests on a.ChangeRequestId equals c.Id
        orderby a.Id
        select new EarningLineDto(a.Id, a.ChangeRequestId, c.Title, a.Hours, a.SharePercent, a.Amount);

    private static decimal Read(IReadOnlyDictionary<string, string> settings, string key, decimal fallback) =>
        settings.TryGetValue(key, out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
