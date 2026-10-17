using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Deductions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;
using ReworkGuard.Domain.Payroll;
using ReworkGuard.Domain.Policies;

namespace ReworkGuard.Application.Deductions;

/// <summary>
/// Keeps deduction lines in step with the facts they come from: approved corrective hours, the manager's review,
/// the attribution split and the pay rate on the work date. Call it inside the caller's transaction after the
/// change has been saved. Lines that are not finalized are cancelled and re-drafted; an applied line that no longer
/// matches gets a compensating line; a line whose amount a manager set in a dispute is only cancelled if the bug
/// stops being deductible.
/// </summary>
public sealed class DeductionLedger
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly AttributionResolver _attribution;
    private readonly HourlyRateResolver _rates;
    private readonly PolicyProvider _policies;
    private readonly PayrollCalendar _payrollCalendar;
    private readonly NotificationPublisher _notifications;

    public DeductionLedger(
        IApplicationDbContext db,
        IClock clock,
        IAuditWriter audit,
        AttributionResolver attribution,
        HourlyRateResolver rates,
        PolicyProvider policies,
        PayrollCalendar payrollCalendar,
        NotificationPublisher notifications)
    {
        _notifications = notifications;
        _db = db;
        _clock = clock;
        _audit = audit;
        _attribution = attribution;
        _rates = rates;
        _policies = policies;
        _payrollCalendar = payrollCalendar;
    }

    public sealed record SyncResult(IReadOnlyList<Deduction> Drafted, IReadOnlyList<Deduction> Cancelled, IReadOnlyList<Deduction> Compensations);

    public async Task<SyncResult> SyncBugsAsync(IReadOnlyCollection<int> bugIds, string trigger, CancellationToken cancellationToken)
    {
        if (bugIds.Count == 0)
        {
            return new SyncResult([], [], []);
        }

        var now = _clock.UtcNow;
        var policy = await _policies.GetSnapshotAsync(cancellationToken);
        var desired = await DesiredLinesAsync(bugIds, policy, cancellationToken);

        var existing = await _db.Deductions
            .Include(d => d.Dispute)
            .Where(d => bugIds.Contains(d.BugId) && d.Kind == DeductionKind.Standard
                        && d.Status != DeductionStatus.Cancelled && d.Status != DeductionStatus.Reversed)
            .ToListAsync(cancellationToken);
        var compensated = await _db.Deductions
            .Where(d => bugIds.Contains(d.BugId) && d.Kind == DeductionKind.Compensating)
            .Select(d => d.ReversesDeductionId!.Value)
            .ToListAsync(cancellationToken);
        var live = existing.Where(d => !compensated.Contains(d.Id)).ToDictionary(d => (d.TimeLogId, d.EmployeeId));
        var decided = (await _db.Deductions
                .Where(d => bugIds.Contains(d.BugId) && d.Kind == DeductionKind.Standard && d.CancelledByDecision)
                .Select(d => new { d.TimeLogId, d.EmployeeId })
                .ToListAsync(cancellationToken))
            .Select(d => (d.TimeLogId, d.EmployeeId))
            .ToHashSet();

        var drafted = new List<Deduction>();
        var cancelled = new List<Deduction>();
        var compensations = new List<Deduction>();

        foreach (var key in live.Keys.Union(desired.Keys).OrderBy(k => k.TimeLogId).ThenBy(k => k.EmployeeId))
        {
            desired.TryGetValue(key, out var want);
            if (live.TryGetValue(key, out var line))
            {
                if (want is not null && (line.AmountSetByDispute || Matches(line, want)))
                {
                    continue;
                }

                if (line.Status == DeductionStatus.Disputed || line.Status == DeductionStatus.Locked)
                {
                    // An open dispute is settled by its resolution, and a locked line by its run; both re-sync afterwards.
                    continue;
                }

                if (line.Status == DeductionStatus.Applied)
                {
                    var compensation = Deduction.Compensate(line, await _payrollCalendar.CompensationMonthForAsync(line, cancellationToken), $"Recalculated after {trigger}", approverId: null, now);
                    _db.Deductions.Add(compensation);
                    compensations.Add(compensation);
                }
                else
                {
                    line.Cancel($"Recalculated after {trigger}", now);
                    cancelled.Add(line);
                }
            }

            if (want is not null && !decided.Contains(key))
            {
                var applyMonth = await _payrollCalendar.ApplyMonthForAsync(YearMonth.From(want.WorkDate), cancellationToken);
                var created = Deduction.Create(want.EmployeeId, want.BugId, want.TimeLogId, want.WorkDate, applyMonth, want.Hours, want.HourlyRate, want.SharePercent, now);
                if (created.Amount > 0)
                {
                    _db.Deductions.Add(created);
                    drafted.Add(created);
                }
            }
        }

        if (drafted.Count + cancelled.Count + compensations.Count == 0)
        {
            return new SyncResult([], [], []);
        }

        await _db.SaveChangesAsync(cancellationToken);
        foreach (var d in drafted)
        {
            Record(d, "Drafted", new { trigger, d.EmployeeId, d.BugId, d.TimeLogId, d.WorkDate, d.Hours, d.HourlyRateUsed, d.SharePercent, d.Amount, d.ApplyMonth, d.RoutedFromLockedMonth });
        }

        foreach (var d in cancelled)
        {
            Record(d, "Cancelled", new { trigger, d.CancellationReason, d.Amount });
        }

        foreach (var d in compensations)
        {
            Record(d, "CompensationRaised", new { trigger, d.ReversesDeductionId, d.Amount, d.ApplyMonth });
        }

        // Employees hear about changes to lines they were already told about; drafts are announced when notified.
        await _notifications.PublishAsync(
            cancelled.Where(d => d.NotifiedAtUtc is not null)
                .Select(d => NotificationTemplates.DeductionCancelled(d.EmployeeId, d.Id, d.Amount, d.CancellationReason!))
                .Concat(compensations.Select(d => NotificationTemplates.CompensationRaised(d.EmployeeId, d.Id, d.Amount, d.ApplyMonth)))
                .ToList(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncResult(drafted, cancelled, compensations);
    }

    /// <summary>Re-syncs every bug on a task (approved regular hours change the computed split).</summary>
    public async Task<SyncResult> SyncTaskAsync(int taskId, string trigger, CancellationToken cancellationToken)
    {
        var bugIds = await _db.Bugs.Where(b => b.TaskId == taskId).Select(b => b.Id).ToListAsync(cancellationToken);
        return await SyncBugsAsync(bugIds, trigger, cancellationToken);
    }

    private sealed record DesiredLine(int EmployeeId, int BugId, int TimeLogId, DateOnly WorkDate, decimal Hours, decimal HourlyRate, decimal SharePercent);

    private async Task<Dictionary<(int TimeLogId, int EmployeeId), DesiredLine>> DesiredLinesAsync(
        IReadOnlyCollection<int> bugIds,
        PolicySnapshot policy,
        CancellationToken cancellationToken)
    {
        var bugs = await _db.Bugs.AsNoTracking()
            .Where(b => bugIds.Contains(b.Id))
            .Select(b => new { b.Id, b.WithinWarranty, b.CausedByOriginalDelivery, b.RootCause })
            .ToListAsync(cancellationToken);
        var deductible = bugs
            .Where(b => DeductibilityPolicy.IsDeductible(b.WithinWarranty, b.CausedByOriginalDelivery, b.RootCause, policy))
            .ToDictionary(b => b.Id, b => b.RootCause!.Value);
        if (deductible.Count == 0)
        {
            return [];
        }

        var deductibleIds = deductible.Keys.ToList();
        var logs = await _db.TimeLogs.AsNoTracking()
            .Where(l => l.WorkType == WorkType.Corrective && l.ApprovalStatus == ApprovalStatus.Approved && deductibleIds.Contains(l.BugId!.Value))
            .Select(l => new { l.Id, BugId = l.BugId!.Value, l.WorkDate, l.Hours, l.WorkType })
            .ToListAsync(cancellationToken);
        if (logs.Count == 0)
        {
            return [];
        }

        var shares = await _attribution.ResolveAsync(deductibleIds, cancellationToken);
        await _rates.PreloadAsync(shares.Values.SelectMany(s => s.Shares).Select(s => s.EmployeeId).ToList(), cancellationToken);

        var result = new Dictionary<(int, int), DesiredLine>();
        foreach (var log in logs)
        {
            DeductionWorkTypeGuard.EnsureDeductible(log.Id, log.WorkType);
            foreach (var share in shares[log.BugId].Shares)
            {
                var effectiveShare = DeductibilityPolicy.EffectiveSharePercent(share.SharePercent, deductible[log.BugId], policy);
                if (effectiveShare <= 0)
                {
                    continue;
                }

                var rate = await _rates.HourlyRateAsync(share.EmployeeId, log.WorkDate, policy.Deduction, cancellationToken);
                result[(log.Id, share.EmployeeId)] = new DesiredLine(share.EmployeeId, log.BugId, log.Id, log.WorkDate, log.Hours, rate, effectiveShare);
            }
        }

        return result;
    }

    private static bool Matches(Deduction line, DesiredLine want) =>
        line.Hours == want.Hours && line.HourlyRateUsed == want.HourlyRate && line.SharePercent == want.SharePercent && line.WorkDate == want.WorkDate;

    private void Record(Deduction d, string action, object details) =>
        _audit.Record(AuditEntities.Deduction, d.Id.ToString(CultureInfo.InvariantCulture), action, details);
}
