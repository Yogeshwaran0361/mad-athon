using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Payroll;

/// <summary>
/// One employee's deductions for a pay month, computed by the same engine and inputs as the payroll run, so the
/// preview and the run never disagree. Employees see their own; team leads their team; managers, Finance and Admin anyone.
/// </summary>
public sealed class DeductionPreviewService
{
    private static readonly DeductionStatus[] PendingStatuses = [DeductionStatus.Draft, DeductionStatus.Notified, DeductionStatus.Disputed, DeductionStatus.Resolved];

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly PayrollInputsBuilder _inputs;
    private readonly PayrollCalendar _calendar;
    private readonly IClock _clock;
    private readonly ProfitShare.ProfitShareService _profitShare;

    public DeductionPreviewService(
        IApplicationDbContext db, ICurrentUser user, PayrollInputsBuilder inputs, PayrollCalendar calendar, IClock clock, ProfitShare.ProfitShareService profitShare)
    {
        _profitShare = profitShare;
        _db = db;
        _user = user;
        _inputs = inputs;
        _calendar = calendar;
        _clock = clock;
    }

    /// <param name="periodMonth">The pay month (yyyy-MM). When omitted, the month this month's corrective work is deducted in.</param>

    public async Task<DeductionPreviewDto> PreviewAsync(int employeeId, string? periodMonth, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        if (employeeId <= 0)
        {
            failures.Add(new ValidationFailure("employeeId", "employeeId is required."));
        }

        YearMonth month = default;
        if (periodMonth is null)
        {
            month = await _calendar.ApplyMonthForAsync(YearMonth.From(WorkTimeZone.ToWorkDate(_clock.UtcNow)), cancellationToken);
        }
        else if (!YearMonth.TryParse(periodMonth, out month))
        {
            failures.Add(new ValidationFailure("periodMonth", "periodMonth must be a month in yyyy-MM format."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        await EmployeeScope.EnsureVisibleAsync(_db, _user, employeeId, cancellationToken);
        var employee = await _db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => new { e.Id, e.Name, e.PayMode }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);

        var monthText = month.ToString();
        var pending = await Details(_db.Deductions.AsNoTracking()
                .Where(d => d.EmployeeId == employeeId && PendingStatuses.Contains(d.Status) && string.Compare(d.ApplyMonth, monthText) <= 0))
            .ToListAsync(cancellationToken);
        var pendingLines = pending.Select(p => Line(p, PayrollItemKind.Current, p.Amount, 0m, 0m)).ToList();

        var finalizedRunId = await _db.PayrollPeriods.AsNoTracking().Where(p => p.Month == monthText && p.IsLocked).Select(p => p.FinalizedRunId).SingleOrDefaultAsync(cancellationToken);
        if (finalizedRunId is { } runId)
        {
            return await FromRunAsync(runId, employee.Id, employee.Name, employee.PayMode, monthText, pendingLines, cancellationToken);
        }

        var inputs = await _inputs.BuildAsync(month, cancellationToken, onlyEmployeeId: employeeId);
        var result = PayrollRunCalculator.Calculate(inputs.Run).Employees.Single();
        var ids = result.Items.Select(i => i.DeductionId).ToList();
        var details = await Details(_db.Deductions.AsNoTracking().Where(d => ids.Contains(d.Id))).ToDictionaryAsync(d => d.Id, cancellationToken);
        return new DeductionPreviewDto(
            employee.Id,
            employee.Name,
            employee.PayMode,
            monthText,
            "Live estimate from lines approved so far",
            null,
            result.SalaryBase,
            inputs.Employees[employee.Id].SalaryBasis,
            inputs.Run.CapPercent,
            result.CapAmount,
            result.CarriedIn,
            result.CurrentDue,
            result.Refunds,
            result.Applied,
            result.CarriedForward,
            result.SettledManually,
            result.NetAdjustment,
            result.Items.Select(i => Line(details[i.DeductionId], i.Kind, i.Due, i.Applied, i.CarriedForward, i.OriginalApplyMonth.ToString())).ToList(),
            pendingLines,
            result.Earnings,
            result.NetPay,
            await _profitShare.LinesAsync(inputs.Run.Employees.Single().Earnings.Select(e => e.AwardId).ToList(), cancellationToken));
    }

    private async Task<DeductionPreviewDto> FromRunAsync(
        int runId, int employeeId, string name, PayMode payMode, string month, List<DeductionPreviewLineDto> pending, CancellationToken cancellationToken)
    {
        var run = await _db.PayrollRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => new { r.RunVersion, r.CapPercent }).SingleAsync(cancellationToken);
        var row = await _db.PayrollRunEmployees.AsNoTracking().Include(e => e.Items).SingleOrDefaultAsync(e => e.PayrollRunId == runId && e.EmployeeId == employeeId, cancellationToken);
        var source = $"Finalized payroll run, version {run.RunVersion}";
        if (row is null)
        {
            return new DeductionPreviewDto(employeeId, name, payMode, month, source, runId, 0m, "Not in this run", run.CapPercent, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, [], pending);
        }

        var ids = row.Items.Select(i => i.DeductionId).ToList();
        var details = await Details(_db.Deductions.AsNoTracking().Where(d => ids.Contains(d.Id))).ToDictionaryAsync(d => d.Id, cancellationToken);
        return new DeductionPreviewDto(
            employeeId, name, payMode, month, source, runId, row.SalaryBase, row.SalaryBasis, run.CapPercent, row.CapAmount, row.CarriedIn, row.CurrentDue,
            row.Refunds, row.Applied, row.CarriedForward, row.SettledManually, row.NetAdjustment,
            row.Items.OrderBy(i => i.Kind).ThenBy(i => i.WorkDate).ThenBy(i => i.DeductionId)
                .Select(i => Line(details[i.DeductionId], i.Kind, i.Due, i.Applied, i.CarriedForward, i.OriginalApplyMonth)).ToList(),
            pending,
            row.Earnings,
            row.SalaryBase + row.Earnings + row.NetAdjustment,
            await _profitShare.PaidInRunAsync(runId, employeeId, cancellationToken));
    }

    private sealed record LineDetail(
        int Id, DeductionStatus Status, string ApplyMonth, DateOnly WorkDate, int BugId, int TaskId, string TaskTitle, string ModuleName,
        decimal Hours, decimal HourlyRate, decimal SharePercent, decimal Amount);

    private IQueryable<LineDetail> Details(IQueryable<Domain.Entities.Deduction> lines) =>
        from d in lines
        join b in _db.Bugs on d.BugId equals b.Id
        orderby d.WorkDate, d.Id
        select new LineDetail(d.Id, d.Status, d.ApplyMonth, d.WorkDate, d.BugId, b.TaskId, b.Task.Title, b.Task.Module.Name, d.Hours, d.HourlyRateUsed, d.SharePercent, d.Amount);

    private static DeductionPreviewLineDto Line(LineDetail d, PayrollItemKind kind, decimal due, decimal applied, decimal carried, string? applyMonth = null) =>
        new(d.Id, kind, d.Status, applyMonth ?? d.ApplyMonth, d.WorkDate, d.BugId, d.TaskId, d.TaskTitle, d.ModuleName, d.Hours, d.HourlyRate, d.SharePercent, due, applied, carried);
}
