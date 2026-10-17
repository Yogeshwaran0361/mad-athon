using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Reports;

/// <summary>
/// Read-only reporting over time logs, bugs, disputes and finalized payroll runs. Every figure is computed by grouped
/// SQL (one query per figure family, never per row); see docs/REPORTS.md for the definitions.
/// </summary>
public sealed class ReportService
{
    public const int MaxRangeMonths = 36;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly PolicyProvider _policies;

    public ReportService(IApplicationDbContext db, ICurrentUser user, PolicyProvider policies)
    {
        _db = db;
        _user = user;
        _policies = policies;
    }

    public async Task<ReworkSummaryReport> ReworkSummaryAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        EnsureAggregateViewer();
        var range = MonthRange.Parse(from, to);
        var scoped = !EmployeeScope.SeesEveryone(_user.Role);
        var visible = EmployeeScope.VisibleEmployeeIds(_db, _user);
        var qaMissDeductible = (await _policies.GetSnapshotAsync(cancellationToken)).QaMissDeductible;

        var logs = ApprovedLogs(range);
        if (scoped)
        {
            logs = logs.Where(t => visible.Contains(t.EmployeeId));
        }

        var hours = await logs
            .GroupBy(t => new { t.WorkDate.Year, t.WorkDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Regular = g.Sum(t => t.WorkType == WorkType.Regular ? t.Hours : 0m),
                Corrective = g.Sum(t => t.WorkType == WorkType.Corrective ? t.Hours : 0m),
            })
            .ToListAsync(cancellationToken);

        var bugs = BugsFound(range);
        if (scoped)
        {
            bugs = bugs.Where(b => _db.TaskAssignments.Any(a => a.TaskId == b.TaskId && visible.Contains(a.EmployeeId)));
        }

        var bugCounts = await bugs
            .GroupBy(b => new { b.FoundDate.Year, b.FoundDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Reported = g.Count(),
                Deductible = g.Count(b => b.WithinWarranty
                    && b.CausedByOriginalDelivery == true
                    && (b.RootCause == RootCause.OwnDefect || (qaMissDeductible && b.RootCause == RootCause.QaMiss))),
            })
            .ToListAsync(cancellationToken);

        var runEmployees = FinalizedRunEmployees(range);
        if (scoped)
        {
            runEmployees = runEmployees.Where(x => visible.Contains(x.EmployeeId));
        }

        var payroll = await runEmployees
            .GroupBy(x => x.PeriodMonth)
            .Select(g => new { Month = g.Key, Applied = g.Sum(x => x.Applied), Refunds = g.Sum(x => x.Refunds) })
            .ToListAsync(cancellationToken);

        var rows = range.Months().Select(m =>
        {
            var h = hours.SingleOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            var b = bugCounts.SingleOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            var p = payroll.SingleOrDefault(x => x.Month == m.ToString());
            var regular = h?.Regular ?? 0m;
            var corrective = h?.Corrective ?? 0m;
            var total = regular + corrective;
            return new ReworkSummaryRow(
                m.ToString(),
                regular,
                corrective,
                total == 0m ? 0m : Math.Round(corrective / total * 100m, 2, MidpointRounding.AwayFromZero),
                b?.Reported ?? 0,
                b?.Deductible ?? 0,
                p?.Applied ?? 0m,
                p?.Refunds ?? 0m);
        }).ToList();

        return new ReworkSummaryReport(range.From.ToString(), range.To.ToString(), rows);
    }

    public async Task<EmployeeReport> ByEmployeeAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        var range = MonthRange.Parse(from, to);
        var scoped = !EmployeeScope.SeesEveryone(_user.Role);
        var visible = EmployeeScope.VisibleEmployeeIds(_db, _user);

        var logs = ApprovedLogs(range).Where(t => t.WorkType == WorkType.Corrective);
        if (scoped)
        {
            logs = logs.Where(t => visible.Contains(t.EmployeeId));
        }

        var corrective = await logs
            .GroupBy(t => t.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Hours = g.Sum(t => t.Hours) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Hours, cancellationToken);

        var runEmployees = FinalizedRunEmployees(range);
        if (scoped)
        {
            runEmployees = runEmployees.Where(x => visible.Contains(x.EmployeeId));
        }

        var applied = await runEmployees
            .GroupBy(x => x.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Applied = g.Sum(x => x.Applied) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Applied, cancellationToken);

        var months = range.MonthTexts();
        var latestRunId = await _db.PayrollPeriods
            .Where(p => p.FinalizedRunId != null && months.Contains(p.Month))
            .OrderByDescending(p => p.Month)
            .Select(p => p.FinalizedRunId)
            .FirstOrDefaultAsync(cancellationToken);
        var carried = new Dictionary<int, decimal>();
        if (latestRunId is { } runId)
        {
            var latest = _db.PayrollRunEmployees.Where(e => e.PayrollRunId == runId);
            if (scoped)
            {
                latest = latest.Where(e => visible.Contains(e.EmployeeId));
            }

            carried = await latest.ToDictionaryAsync(e => e.EmployeeId, e => e.CarriedForward, cancellationToken);
        }

        var (fromUtc, toUtcExclusive) = range.UtcBounds();
        var disputeQuery = _db.DeductionDisputes.Where(d => d.RaisedAtUtc >= fromUtc && d.RaisedAtUtc < toUtcExclusive);
        if (scoped)
        {
            disputeQuery = disputeQuery.Where(d => visible.Contains(d.RaisedByEmployeeId));
        }

        var disputes = await disputeQuery
            .GroupBy(d => d.RaisedByEmployeeId)
            .Select(g => new { EmployeeId = g.Key, Raised = g.Count(), Upheld = g.Count(d => d.Outcome == DisputeOutcome.Upheld) })
            .ToDictionaryAsync(x => x.EmployeeId, cancellationToken);

        var ids = corrective.Where(x => x.Value != 0m).Select(x => x.Key)
            .Concat(applied.Where(x => x.Value != 0m).Select(x => x.Key))
            .Concat(carried.Where(x => x.Value != 0m).Select(x => x.Key))
            .Concat(disputes.Keys)
            .Distinct()
            .ToList();
        var names = await _db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);

        var rows = ids
            .Select(id => new EmployeeReportRow(
                id,
                names[id],
                corrective.GetValueOrDefault(id),
                applied.GetValueOrDefault(id),
                carried.GetValueOrDefault(id),
                disputes.TryGetValue(id, out var d) ? d.Raised : 0,
                d?.Upheld ?? 0))
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeId)
            .ToList();
        return new EmployeeReport(range.From.ToString(), range.To.ToString(), rows);
    }

    public async Task<ModuleReport> ByModuleAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        EnsureAggregateViewer();
        var range = MonthRange.Parse(from, to);
        var scoped = !EmployeeScope.SeesEveryone(_user.Role);
        var visible = EmployeeScope.VisibleEmployeeIds(_db, _user);

        var bugs = BugsFound(range);
        if (scoped)
        {
            bugs = bugs.Where(b => _db.TaskAssignments.Any(a => a.TaskId == b.TaskId && visible.Contains(a.EmployeeId)));
        }

        var bugCounts = await bugs
            .GroupBy(b => b.Task.ModuleId)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Count, cancellationToken);

        var (fromUtc, toUtcExclusive) = range.UtcBounds();
        var reopenQuery = from h in _db.TaskStateHistory
                          join t in _db.WorkTasks on h.TaskId equals t.Id
                          where h.ToStatus == WorkTaskStatus.Reopened && h.ChangedAtUtc >= fromUtc && h.ChangedAtUtc < toUtcExclusive
                          select new { t.Id, t.ModuleId };
        if (scoped)
        {
            reopenQuery = reopenQuery.Where(x => _db.TaskAssignments.Any(a => a.TaskId == x.Id && visible.Contains(a.EmployeeId)));
        }

        var reopens = await reopenQuery
            .GroupBy(x => x.ModuleId)
            .Select(g => new { ModuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Count, cancellationToken);

        var logs = ApprovedLogs(range).Where(t => t.WorkType == WorkType.Corrective);
        if (scoped)
        {
            logs = logs.Where(t => visible.Contains(t.EmployeeId));
        }

        var hours = await logs
            .GroupBy(t => t.Task.ModuleId)
            .Select(g => new { ModuleId = g.Key, Hours = g.Sum(t => t.Hours) })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Hours, cancellationToken);

        var runIds = FinalizedRunIds(range);
        var itemQuery = from i in _db.PayrollRunItems
                        join e in _db.PayrollRunEmployees on i.PayrollRunEmployeeId equals e.Id
                        join d in _db.Deductions on i.DeductionId equals d.Id
                        join b in _db.Bugs on d.BugId equals b.Id
                        join t in _db.WorkTasks on b.TaskId equals t.Id
                        where runIds.Contains(e.PayrollRunId)
                        select new { e.EmployeeId, t.ModuleId, i.Applied };
        if (scoped)
        {
            itemQuery = itemQuery.Where(x => visible.Contains(x.EmployeeId));
        }

        var amounts = await itemQuery
            .GroupBy(x => x.ModuleId)
            .Select(g => new { ModuleId = g.Key, Amount = g.Sum(x => x.Applied) })
            .ToDictionaryAsync(x => x.ModuleId, x => x.Amount, cancellationToken);

        var moduleIds = bugCounts.Keys.Concat(reopens.Keys).Concat(hours.Keys).Concat(amounts.Keys).Distinct().ToList();
        var modules = await _db.Modules
            .Where(m => moduleIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, ProjectName = m.Project.Name })
            .ToListAsync(cancellationToken);

        var rows = modules
            .Select(m => new ModuleReportRow(
                m.ProjectName,
                m.Name,
                bugCounts.GetValueOrDefault(m.Id),
                reopens.GetValueOrDefault(m.Id),
                hours.GetValueOrDefault(m.Id),
                amounts.GetValueOrDefault(m.Id)))
            .OrderBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.ModuleName, StringComparer.Ordinal)
            .ToList();
        return new ModuleReport(range.From.ToString(), range.To.ToString(), rows);
    }

    public async Task<PayrollRegisterReport> PayrollRegisterAsync(string? periodMonth, CancellationToken cancellationToken)
    {
        if (!EmployeeScope.SeesEveryone(_user.Role))
        {
            throw new ForbiddenException("The payroll register is available to Manager, Finance and Admin.");
        }

        var month = MonthRange.ParseMonth(periodMonth, "periodMonth").ToString();
        var run = await _db.PayrollPeriods
            .Where(p => p.Month == month && p.FinalizedRunId != null)
            .Join(_db.PayrollRuns, p => p.FinalizedRunId, r => r.Id, (p, r) => new { r.Id, r.RunVersion })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("report.month_not_finalized", $"Payroll for {month} has not been finalized.");

        var rows = await _db.PayrollRunEmployees
            .Where(e => e.PayrollRunId == run.Id)
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new PayrollRegisterRow(
                e.EmployeeCode,
                e.EmployeeName,
                e.PayMode,
                e.SalaryBase,
                e.CapAmount,
                e.CarriedIn,
                e.CurrentDue,
                e.Refunds,
                e.Applied,
                e.CarriedForward,
                e.SettledManually,
                e.NetAdjustment))
            .ToListAsync(cancellationToken);
        return new PayrollRegisterReport(month, run.Id, run.RunVersion, rows);
    }

    private void EnsureAggregateViewer()
    {
        if (_user.Role is Role.Employee or Role.QA)
        {
            throw new ForbiddenException("This report is available to team leads, managers, finance and admins.");
        }
    }

    private IQueryable<Domain.Entities.TimeLog> ApprovedLogs(MonthRange range)
    {
        var first = range.From.FirstDay;
        var last = range.To.LastDay;
        return _db.TimeLogs.Where(t => t.ApprovalStatus == ApprovalStatus.Approved && t.WorkDate >= first && t.WorkDate <= last);
    }

    private IQueryable<Domain.Entities.Bug> BugsFound(MonthRange range)
    {
        var first = range.From.FirstDay;
        var last = range.To.LastDay;
        return _db.Bugs.Where(b => b.FoundDate >= first && b.FoundDate <= last);
    }

    /// <summary>The run each month in range was finalized with (Finalized or Locked status).</summary>
    private IQueryable<int> FinalizedRunIds(MonthRange range)
    {
        var months = range.MonthTexts();
        return _db.PayrollPeriods.Where(p => p.FinalizedRunId != null && months.Contains(p.Month)).Select(p => p.FinalizedRunId!.Value);
    }

    private IQueryable<RunEmployeeRow> FinalizedRunEmployees(MonthRange range)
    {
        var runIds = FinalizedRunIds(range);
        return from e in _db.PayrollRunEmployees
               join r in _db.PayrollRuns on e.PayrollRunId equals r.Id
               where runIds.Contains(r.Id)
               select new RunEmployeeRow { PeriodMonth = r.PeriodMonth, EmployeeId = e.EmployeeId, Applied = e.Applied, Refunds = e.Refunds };
    }

    private sealed class RunEmployeeRow
    {
        public string PeriodMonth { get; init; } = string.Empty;

        public int EmployeeId { get; init; }

        public decimal Applied { get; init; }

        public decimal Refunds { get; init; }
    }
}

/// <summary>An inclusive range of months taken from yyyy-MM query parameters.</summary>
public readonly record struct MonthRange(YearMonth From, YearMonth To)
{
    public static MonthRange Parse(string? from, string? to)
    {
        var failures = new List<ValidationFailure>();
        var f = TryParse(from, "from", failures);
        var t = TryParse(to, "to", failures);
        if (failures.Count == 0)
        {
            if (f > t)
            {
                failures.Add(new ValidationFailure("from", "from must not be after to."));
            }
            else if (Count(f, t) > ReportService.MaxRangeMonths)
            {
                failures.Add(new ValidationFailure("to", $"The range can span at most {ReportService.MaxRangeMonths} months."));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return new MonthRange(f, t);
    }

    public static YearMonth ParseMonth(string? value, string name)
    {
        var failures = new List<ValidationFailure>();
        var month = TryParse(value, name, failures);
        return failures.Count > 0 ? throw new ValidationException(failures) : month;
    }

    public IEnumerable<YearMonth> Months()
    {
        for (var m = From; m <= To; m = m.Next())
        {
            yield return m;
        }
    }

    public List<string> MonthTexts() => Months().Select(m => m.ToString()).ToList();

    /// <summary>UTC instants bounding the range in the work time zone (IST): [start of From, start of the month after To).</summary>
    public (DateTime FromUtc, DateTime ToUtcExclusive) UtcBounds() =>
        (WorkTimeZone.StartOfDayUtc(From.FirstDay), WorkTimeZone.StartOfDayUtc(To.Next().FirstDay));

    private static int Count(YearMonth from, YearMonth to) => (to.Year * 12 + to.Month) - (from.Year * 12 + from.Month) + 1;

    private static YearMonth TryParse(string? value, string name, List<ValidationFailure> failures)
    {
        if (value is { Length: 7 } && YearMonth.TryParse(value, out var month))
        {
            return month;
        }

        failures.Add(new ValidationFailure(name, $"{name} must be a month in yyyy-MM format."));
        return default;
    }
}
