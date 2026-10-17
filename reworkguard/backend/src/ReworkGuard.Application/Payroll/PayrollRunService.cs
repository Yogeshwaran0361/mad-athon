using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Payroll;

/// <summary>
/// The monthly payroll run: generate (and regenerate) a Preview, Finance review, Manager approval, finalization and lock.
/// Generation and finalization for a month are serialised by a row lock on the month; every step checks the run
/// version the caller saw. Months are run in order: once a month is finalized, only the following month can be run.
/// </summary>
public sealed class PayrollRunService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly PayrollInputsBuilder _inputs;
    private readonly PayrollCalendar _calendar;
    private readonly PolicyProvider _policies;
    private readonly NotificationPublisher _notifications;
    private readonly DeductionLedger _ledger;
    private readonly IValidator<GeneratePayrollRunRequest> _generateValidator;
    private readonly IValidator<PayrollRunStepRequest> _stepValidator;

    public PayrollRunService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IAuditWriter audit,
        PayrollInputsBuilder inputs,
        PayrollCalendar calendar,
        PolicyProvider policies,
        NotificationPublisher notifications,
        DeductionLedger ledger,
        IValidator<GeneratePayrollRunRequest> generateValidator,
        IValidator<PayrollRunStepRequest> stepValidator)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _inputs = inputs;
        _calendar = calendar;
        _policies = policies;
        _notifications = notifications;
        _ledger = ledger;
        _generateValidator = generateValidator;
        _stepValidator = stepValidator;
    }

    public async Task<PayrollRunDetailDto> GenerateAsync(GeneratePayrollRunRequest request, CancellationToken cancellationToken)
    {
        await _generateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var month = YearMonth.Parse(request.PeriodMonth);
        var latestAllowed = YearMonth.From(WorkTimeZone.ToWorkDate(_clock.UtcNow)).Next();
        if (month > latestAllowed)
        {
            throw new DomainException("payroll.too_early", $"Payroll can be run up to {latestAllowed}.");
        }

        await _db.EnsurePayrollPeriodAsync(month.ToString(), cancellationToken);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _db.LockPayrollPeriodAsync(month.ToString(), cancellationToken);
        await EnsureInSequenceAsync(month, cancellationToken);

        var inputs = await _inputs.BuildAsync(month, cancellationToken);
        var result = PayrollRunCalculator.Calculate(inputs.Run);
        var hash = PayrollInputsHash.Compute(inputs.Run);
        var now = _clock.UtcNow;

        var monthText = month.ToString();
        var existing = await _db.PayrollRuns.Where(r => r.PeriodMonth == monthText).ToListAsync(cancellationToken);
        foreach (var open in existing.Where(r => r.IsOpen))
        {
            open.Supersede(now);
            Record(open, "Superseded", new { open.RunVersion });
        }

        var run = new PayrollRun(month, existing.Count == 0 ? 1 : existing.Max(r => r.RunVersion) + 1, hash, result, inputs.Employees, _user.EmployeeId, now);
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        Record(run, "Generated", new
        {
            run.PeriodMonth,
            run.RunVersion,
            run.InputsHash,
            run.CapPercent,
            run.EmployeeCount,
            run.TotalApplied,
            run.TotalCarriedForward,
            run.TotalRefunds,
            run.TotalSettledManually,
            PreviousRunId = inputs.PreviousRunId,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(run.Id, cancellationToken);
    }

    public async Task<PayrollRunDetailDto> ReviewAsync(int id, PayrollRunStepRequest request, CancellationToken cancellationToken)
    {
        var run = await LoadForStepAsync(id, request, cancellationToken);
        run.MarkReviewed(_user.EmployeeId, _clock.UtcNow);
        Record(run, "Reviewed", new { run.RunVersion, run.InputsHash });
        var managers = await _notifications.ActiveEmployeesInRoleAsync(Role.Manager, cancellationToken);
        await _notifications.PublishAsync(managers.Select(m => PayrollTemplates.AwaitingApproval(m, run)).ToList(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<PayrollRunDetailDto> ApproveAsync(int id, PayrollRunStepRequest request, CancellationToken cancellationToken)
    {
        var run = await LoadForStepAsync(id, request, cancellationToken);
        var inputs = await _inputs.BuildAsync(YearMonth.Parse(run.PeriodMonth), cancellationToken);
        run.Approve(_user.EmployeeId, PayrollInputsHash.Compute(inputs.Run), _clock.UtcNow);
        Record(run, "Approved", new { run.RunVersion, run.ApprovedInputsHash });
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Re-checks the inputs against the approved hash, runs the reconciliation, moves the run's lines to Locked and locks
    /// the month so later lines go to the next one. The previous month's run must already be locked.
    /// </summary>
    public async Task<PayrollRunDetailDto> FinalizeAsync(int id, PayrollRunStepRequest request, CancellationToken cancellationToken)
    {
        await _stepValidator.ValidateAndThrowAsync(request, cancellationToken);
        var month = await _db.PayrollRuns.AsNoTracking().Where(r => r.Id == id).Select(r => r.PeriodMonth).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Payroll run", id);

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _db.LockPayrollPeriodAsync(month, cancellationToken);
        var run = await LoadForStepAsync(id, request, cancellationToken);
        if (run.Status != PayrollRunStatus.Approved)
        {
            throw new DomainException("payroll.invalid_transition", $"A {run.Status} run cannot be finalized; it must be Approved.");
        }

        var period = await _db.PayrollPeriods.SingleAsync(p => p.Month == month, cancellationToken);
        var inputs = await _inputs.BuildAsync(YearMonth.Parse(month), cancellationToken);
        decimal previousCarried = 0m;
        if (inputs.PreviousRunId is { } previousId)
        {
            var previous = await _db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == previousId, cancellationToken);
            if (previous.Status != PayrollRunStatus.Locked)
            {
                throw new DomainException("payroll.previous_not_locked", $"Lock the {previous.PeriodMonth} run before finalizing {month}.");
            }

            previousCarried = previous.TotalCarriedForward;
        }

        var hash = PayrollInputsHash.Compute(inputs.Run);
        if (hash != run.ApprovedInputsHash)
        {
            throw new DomainException("payroll.inputs_changed", "Deduction lines, pay or policy changed since this run was approved. Generate a new version.");
        }

        var lineIds = run.Employees.SelectMany(e => e.Items).Where(i => i.Kind != PayrollItemKind.CarriedIn).Select(i => i.DeductionId).ToList();
        var lines = await _db.Deductions.Where(d => lineIds.Contains(d.Id)).ToListAsync(cancellationToken);
        var available = lines.Where(d => d.Status == DeductionStatus.Approved && d.PayrollRunId == null).Select(d => d.Id).ToList();
        var checks = PayrollReconciliation.Check(
            run,
            PayrollRunCalculator.Calculate(inputs.Run),
            available,
            previousCarried,
            run.Employees.Sum(e => e.CarriedIn));
        var failed = checks.Where(c => !c.Passed).ToList();
        if (failed.Count > 0)
        {
            throw new DomainException("payroll.reconciliation_failed", "Reconciliation failed: " + string.Join("; ", failed.Select(c => $"{c.Name}: {c.Detail}")));
        }

        var now = _clock.UtcNow;
        run.Finalize(_user.EmployeeId, hash, JsonSerializer.Serialize(checks), now);
        foreach (var line in lines)
        {
            line.Lock(run.Id);
        }

        var awardIds = inputs.Run.Employees.SelectMany(e => e.Earnings).Select(a => a.AwardId).ToList();
        foreach (var award in await _db.IncentiveAwards.Where(a => awardIds.Contains(a.Id)).ToListAsync(cancellationToken))
        {
            award.MarkPaid(run.Id);
        }

        period.LockFor(run, now);
        Record(run, "Finalized", new { run.RunVersion, run.InputsHash, Lines = lines.Count, Checks = checks.Select(c => c.Name) });
        var finance = await _notifications.ActiveEmployeesInRoleAsync(Role.Finance, cancellationToken);
        await _notifications.PublishAsync(
            finance.Append(run.ApprovedByEmployeeId!.Value).Distinct().Select(f => PayrollTemplates.Finalized(f, run)).ToList(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Applies the run's lines (compensated originals become Reversed). Irreversible. Facts that changed while the lines
    /// were locked (an attribution override, a re-review) were held back by the ledger; they are re-synced now, which
    /// raises compensating lines for the next open month.
    /// </summary>
    public async Task<PayrollRunDetailDto> LockAsync(int id, PayrollRunStepRequest request, CancellationToken cancellationToken)
    {
        await _stepValidator.ValidateAndThrowAsync(request, cancellationToken);
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var run = await LoadForStepAsync(id, request, cancellationToken);
        var now = _clock.UtcNow;
        var requireAck = (await _policies.GetSnapshotAsync(cancellationToken)).RequireEmployeeAcknowledgement;
        var lines = await _db.Deductions.Where(d => d.PayrollRunId == run.Id && d.Status == DeductionStatus.Locked).ToListAsync(cancellationToken);
        run.Lock(_user.EmployeeId, now);
        foreach (var line in lines.OrderBy(l => l.Id))
        {
            try
            {
                line.Apply(requireAck, now);
            }
            catch (DomainException ex)
            {
                throw new DomainException(ex.Code, $"Line {line.Id}: {ex.Message}");
            }
        }

        var reversedIds = lines.Where(l => l.Kind == DeductionKind.Compensating).Select(l => l.ReversesDeductionId!.Value).ToList();
        var originals = await _db.Deductions.Where(d => reversedIds.Contains(d.Id) && d.Status == DeductionStatus.Applied).ToListAsync(cancellationToken);
        foreach (var original in originals)
        {
            original.MarkReversed(now);
        }

        Record(run, "Locked", new { run.RunVersion, Applied = lines.Count, Reversed = originals.Count });

        // Payslips are published now. Everyone in the run has one; only those with rework lines are notified.
        await _notifications.PublishAsync(
            run.Employees.Where(e => e.Items.Count > 0).OrderBy(e => e.EmployeeId).Select(e => PayrollTemplates.PayslipPublished(e, run)).ToList(),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await _ledger.SyncBugsAsync(lines.Select(l => l.BugId).Distinct().ToList(), "payroll run locked", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollRunSummaryDto>> ListAsync(string? periodMonth, CancellationToken cancellationToken)
    {
        var runs = _db.PayrollRuns.AsNoTracking();
        if (periodMonth is not null)
        {
            if (!YearMonth.TryParse(periodMonth, out var month))
            {
                throw new ValidationException([new FluentValidation.Results.ValidationFailure("periodMonth", "periodMonth must be yyyy-MM.")]);
            }

            var text = month.ToString();
            runs = runs.Where(r => r.PeriodMonth == text);
        }

        return await runs.OrderByDescending(r => r.PeriodMonth).ThenByDescending(r => r.RunVersion).Select(SummaryProjection).ToListAsync(cancellationToken);
    }

    public async Task<PayrollRunDetailDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var summary = await _db.PayrollRuns.AsNoTracking().Where(r => r.Id == id).Select(SummaryProjection).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Payroll run", id);
        var employees = await _db.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == id)
            .OrderBy(e => e.EmployeeName).ThenBy(e => e.EmployeeId)
            .Select(EmployeeProjection)
            .ToListAsync(cancellationToken);
        var json = await _db.PayrollRuns.AsNoTracking().Where(r => r.Id == id).Select(r => r.ReconciliationJson).SingleAsync(cancellationToken);
        return new PayrollRunDetailDto(summary, employees, json is null ? null : JsonSerializer.Deserialize<List<ReconciliationCheckDto>>(json));
    }

    public async Task<PayrollRunEmployeeDetailDto> GetEmployeeAsync(int runId, int employeeId, CancellationToken cancellationToken)
    {
        var employee = await _db.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId && e.EmployeeId == employeeId)
            .Select(EmployeeProjection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Payroll run employee", $"{runId}/{employeeId}");
        var items = await (
                from i in _db.PayrollRunItems.AsNoTracking()
                where i.PayrollRunEmployeeId == employee.Id
                join d in _db.Deductions on i.DeductionId equals d.Id
                orderby i.Kind, i.WorkDate, i.DeductionId
                select new PayrollRunItemDto(
                    i.DeductionId, i.Kind, i.OriginalApplyMonth, i.WorkDate, d.BugId, _db.Bugs.Where(b => b.Id == d.BugId).Select(b => b.TaskId).First(),
                    _db.Bugs.Where(b => b.Id == d.BugId).Select(b => b.Task.Title).First(), i.Due, i.Applied, i.CarriedForward, i.SettledManually, i.CancelledByCompensation))
            .ToListAsync(cancellationToken);
        return new PayrollRunEmployeeDetailDto(employee, items);
    }

    public async Task<IReadOnlyList<PayrollPeriodDto>> PeriodsAsync(CancellationToken cancellationToken) =>
        await _db.PayrollPeriods.AsNoTracking()
            .OrderByDescending(p => p.Month)
            .Select(p => new PayrollPeriodDto(p.Month, p.IsLocked, p.FinalizedRunId, p.LockedAtUtc))
            .ToListAsync(cancellationToken);

    private async Task EnsureInSequenceAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var period = await _db.PayrollPeriods.AsNoTracking().SingleAsync(p => p.Month == month.ToString(), cancellationToken);
        if (period.IsLocked)
        {
            throw new DomainException("payroll.month_locked", $"Payroll for {month} is already finalized.");
        }

        var firstOpen = await _calendar.FirstOpenMonthAsync(cancellationToken);
        if (firstOpen is { } open && month != open)
        {
            throw new DomainException("payroll.out_of_sequence", $"Months are run in order: the next payroll to run is {open}.");
        }
    }

    private async Task<PayrollRun> LoadForStepAsync(int id, PayrollRunStepRequest request, CancellationToken cancellationToken)
    {
        await _stepValidator.ValidateAndThrowAsync(request, cancellationToken);
        var run = await _db.PayrollRuns.Include(r => r.Employees).ThenInclude(e => e.Items).SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Payroll run", id);
        if (run.Version != request.ExpectedVersion)
        {
            throw new DomainException("payroll.stale", $"The run changed since you loaded it (version {run.Version}, you sent {request.ExpectedVersion}). Reload and try again.");
        }

        return run;
    }

    private void Record(PayrollRun run, string action, object details) =>
        _audit.Record(AuditEntities.PayrollRun, run.Id.ToString(CultureInfo.InvariantCulture), action, details);

    private static readonly System.Linq.Expressions.Expression<Func<PayrollRun, PayrollRunSummaryDto>> SummaryProjection = r => new PayrollRunSummaryDto(
        r.Id, r.PeriodMonth, r.RunVersion, r.Status, r.InputsHash, r.CapPercent, r.EmployeeCount, r.TotalApplied, r.TotalCarriedForward, r.TotalRefunds,
        r.TotalSettledManually, r.GeneratedByEmployeeId, r.GeneratedAtUtc, r.ReviewedByEmployeeId, r.ReviewedAtUtc, r.ApprovedByEmployeeId, r.ApprovedAtUtc,
        r.FinalizedByEmployeeId, r.FinalizedAtUtc, r.LockedByEmployeeId, r.LockedAtUtc, r.SupersededAtUtc, r.Version);

    private static readonly System.Linq.Expressions.Expression<Func<PayrollRunEmployee, PayrollRunEmployeeDto>> EmployeeProjection = e => new PayrollRunEmployeeDto(
        e.Id, e.EmployeeId, e.EmployeeCode, e.EmployeeName, e.PayMode, e.SalaryBasis, e.ManualSettlement, e.SalaryBase, e.CapAmount, e.CarriedIn,
        e.CurrentDue, e.Refunds, e.CancelledByCompensation, e.Applied, e.CarriedForward, e.SettledManually, e.NetAdjustment, e.Earnings, e.SalaryBase + e.Earnings + e.NetAdjustment);
}
