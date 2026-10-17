using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Payslips;

/// <summary>
/// Payslips are published when a payroll run is locked: one per employee row of the run. Employees see their own;
/// Managers, Finance and Admin see everyone's. Team leads do not see their team's pay. Each PDF download is audited.
/// </summary>
public sealed class PayslipService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly PayrollRunService _runs;
    private readonly IPayslipPdfRenderer _renderer;
    private readonly ProfitShare.ProfitShareService _profitShare;

    public PayslipService(
        IApplicationDbContext db, ICurrentUser user, IClock clock, IAuditWriter audit, PayrollRunService runs, IPayslipPdfRenderer renderer, ProfitShare.ProfitShareService profitShare)
    {
        _profitShare = profitShare;
        _db = db;
        _user = user;
        _clock = clock;
        _audit = audit;
        _runs = runs;
        _renderer = renderer;
    }

    public async Task<IReadOnlyList<PayslipSummaryDto>> ListAsync(int? employeeId, string? periodMonth, CancellationToken cancellationToken)
    {
        string? month = null;
        if (periodMonth is not null)
        {
            if (!YearMonth.TryParse(periodMonth, out var parsed))
            {
                throw new ValidationException([new ValidationFailure("periodMonth", "periodMonth must be a month in yyyy-MM format.")]);
            }

            month = parsed.ToString();
        }

        var query = Published();
        if (!EmployeeScope.SeesEveryone(_user.Role))
        {
            if (employeeId is { } other && other != _user.EmployeeId)
            {
                throw new DomainForbiddenException("payslip.not_yours", "You can only see your own payslips.");
            }

            employeeId = _user.EmployeeId;
        }

        if (employeeId is { } id)
        {
            query = query.Where(p => p.Row.EmployeeId == id);
        }

        if (month is not null)
        {
            query = query.Where(p => p.Run.PeriodMonth == month);
        }

        return await query
            .OrderByDescending(p => p.Run.PeriodMonth).ThenBy(p => p.Row.EmployeeName).ThenBy(p => p.Row.EmployeeId)
            .Select(Summary)
            .ToListAsync(cancellationToken);
    }

    public async Task<PayslipDetailDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        var summary = await Published().Where(p => p.Row.Id == id).Select(Summary).SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Payslip", id);
        if (!EmployeeScope.SeesEveryone(_user.Role) && summary.EmployeeId != _user.EmployeeId)
        {
            throw new DomainForbiddenException("payslip.not_yours", "You can only see your own payslips.");
        }

        var detail = await _runs.GetEmployeeAsync(summary.PayrollRunId, summary.EmployeeId, cancellationToken);
        return new PayslipDetailDto(summary, detail.Employee, detail.Items, await _profitShare.PaidInRunAsync(summary.PayrollRunId, summary.EmployeeId, cancellationToken));
    }

    public async Task<(byte[] Content, string FileName)> PdfAsync(long id, CancellationToken cancellationToken)
    {
        var detail = await GetAsync(id, cancellationToken);
        var capPercent = await _db.PayrollRuns.AsNoTracking().Where(r => r.Id == detail.Payslip.PayrollRunId).Select(r => r.CapPercent).SingleAsync(cancellationToken);
        var pdf = _renderer.Render(new PayslipDocument(detail, detail.Employee.SalaryBasis, capPercent, _clock.UtcNow));
        _audit.Record(AuditEntities.Payslip, id.ToString(CultureInfo.InvariantCulture), "Downloaded", new { detail.Payslip.EmployeeId, detail.Payslip.PeriodMonth, detail.Payslip.PayrollRunId });
        await _db.SaveChangesAsync(cancellationToken);
        return (pdf, $"payslip_{detail.Payslip.EmployeeCode}_{detail.Payslip.PeriodMonth}.pdf");
    }

    private static readonly System.Linq.Expressions.Expression<Func<Published, PayslipSummaryDto>> Summary = p => new PayslipSummaryDto(
        p.Row.Id, p.Row.EmployeeId, p.Row.EmployeeCode, p.Row.EmployeeName, p.Row.PayMode, p.Run.PeriodMonth, p.Run.Id, p.Run.RunVersion, p.Row.SalaryBase,
        p.Row.CapAmount, p.Row.Applied, p.Row.Refunds, p.Row.CarriedForward, p.Row.SettledManually, p.Row.NetAdjustment, p.Run.LockedAtUtc!.Value,
        p.Row.Earnings, p.Row.SalaryBase + p.Row.Earnings + p.Row.NetAdjustment);

    private IQueryable<Published> Published() =>
        from e in _db.PayrollRunEmployees.AsNoTracking()
        join r in _db.PayrollRuns.AsNoTracking() on e.PayrollRunId equals r.Id
        where r.Status == PayrollRunStatus.Locked
        select new Published { Row = e, Run = r };
}

internal sealed class Published
{
    public PayrollRunEmployee Row { get; init; } = null!;

    public PayrollRun Run { get; init; } = null!;
}
