using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Payroll;

/// <summary>Which payroll month a deduction line falls due in.</summary>
public sealed class PayrollCalendar
{
    private readonly IApplicationDbContext _db;

    public PayrollCalendar(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Corrective work done in <paramref name="sourceMonth"/> is deducted in the following month's payroll, unless that
    /// month is already finalized; then it goes to the first month that is still open.
    /// </summary>
    public async Task<YearMonth> ApplyMonthForAsync(YearMonth sourceMonth, CancellationToken cancellationToken) =>
        Later(sourceMonth.Next(), await FirstOpenMonthAsync(cancellationToken));

    /// <summary>
    /// A compensating line goes into the first open month after the month the original line was actually paid in
    /// (its payroll run's month, which can be later than its apply month).
    /// </summary>
    public async Task<YearMonth> CompensationMonthForAsync(Deduction original, CancellationToken cancellationToken)
    {
        var paidIn = original.PayrollRunId is { } runId
            ? await _db.PayrollRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => r.PeriodMonth).SingleAsync(cancellationToken)
            : original.ApplyMonth;
        return Later(YearMonth.Parse(paidIn).Next(), await FirstOpenMonthAsync(cancellationToken));
    }

    /// <summary>The month after the latest finalized one, or null when no payroll has been finalized yet.</summary>
    public async Task<YearMonth?> FirstOpenMonthAsync(CancellationToken cancellationToken)
    {
        var latest = await LatestLockedMonthAsync(cancellationToken);
        return latest?.Next();
    }

    public async Task<YearMonth?> LatestLockedMonthAsync(CancellationToken cancellationToken)
    {
        var latest = await _db.PayrollPeriods.AsNoTracking().Where(p => p.IsLocked).MaxAsync(p => (string?)p.Month, cancellationToken);
        return latest is null ? null : YearMonth.Parse(latest);
    }

    private static YearMonth Later(YearMonth natural, YearMonth? firstOpen) => firstOpen is { } open && open > natural ? open : natural;
}
