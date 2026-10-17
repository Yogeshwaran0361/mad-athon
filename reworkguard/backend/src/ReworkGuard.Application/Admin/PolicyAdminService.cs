using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Policies;

namespace ReworkGuard.Application.Admin;

/// <summary>
/// Policy settings, per-project warranty windows and the holiday calendar. Every change is audited with its before and
/// after values. Changes apply to computations made afterwards: payroll runs store the policy snapshot they used, and
/// bugs, deduction lines and dispute windows keep the values computed when they were created.
/// </summary>
public sealed class PolicyAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly PolicyProvider _policies;
    private readonly IValidator<PolicyDto> _policyValidator;
    private readonly IValidator<UpdateWarrantyRequest> _warrantyValidator;
    private readonly IValidator<CreateHolidayRequest> _holidayValidator;

    public PolicyAdminService(
        IApplicationDbContext db,
        IAuditWriter audit,
        PolicyProvider policies,
        IValidator<PolicyDto> policyValidator,
        IValidator<UpdateWarrantyRequest> warrantyValidator,
        IValidator<CreateHolidayRequest> holidayValidator)
    {
        _db = db;
        _audit = audit;
        _policies = policies;
        _policyValidator = policyValidator;
        _warrantyValidator = warrantyValidator;
        _holidayValidator = holidayValidator;
    }

    public async Task<PolicyDto> GetAsync(CancellationToken cancellationToken) => ToDto(await _policies.GetSnapshotAsync(cancellationToken));

    public async Task<PolicyDto> UpdateAsync(PolicyDto request, CancellationToken cancellationToken)
    {
        await _policyValidator.ValidateAndThrowAsync(request, cancellationToken);
        _ = ToSnapshot(request);

        var before = await GetAsync(cancellationToken);
        var settings = await _db.PolicySettings.ToDictionaryAsync(p => p.Key, cancellationToken);
        foreach (var (key, value) in Values(request))
        {
            if (settings.TryGetValue(key, out var setting))
            {
                setting.Change(value);
            }
            else
            {
                _db.PolicySettings.Add(new PolicySetting(key, value));
            }
        }

        _audit.Record(AuditEntities.Policy, "settings", "PolicyChanged", new { Before = before, After = request });
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectWarrantyDto>> ProjectsAsync(CancellationToken cancellationToken) =>
        await _db.Projects.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectWarrantyDto(p.Id, p.Name, p.WarrantyDays))
            .ToListAsync(cancellationToken);

    public async Task<ProjectWarrantyDto> UpdateWarrantyAsync(int projectId, UpdateWarrantyRequest request, CancellationToken cancellationToken)
    {
        await _warrantyValidator.ValidateAndThrowAsync(request, cancellationToken);
        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project", projectId);
        var before = project.WarrantyDays;
        project.ChangeWarranty(request.WarrantyDays);
        _audit.Record("Project", projectId.ToString(CultureInfo.InvariantCulture), "WarrantyChanged", new { Before = before, After = project.WarrantyDays });
        await _db.SaveChangesAsync(cancellationToken);
        return new ProjectWarrantyDto(project.Id, project.Name, project.WarrantyDays);
    }

    public async Task<IReadOnlyList<HolidayDto>> HolidaysAsync(int? year, CancellationToken cancellationToken)
    {
        var holidays = _db.Holidays.AsNoTracking();
        if (year is not null)
        {
            var from = new DateOnly(year.Value, 1, 1);
            var to = new DateOnly(year.Value, 12, 31);
            holidays = holidays.Where(h => h.Date >= from && h.Date <= to);
        }

        return await holidays.OrderBy(h => h.Date).Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToListAsync(cancellationToken);
    }

    public async Task<HolidayDto> AddHolidayAsync(CreateHolidayRequest request, CancellationToken cancellationToken)
    {
        await _holidayValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (await _db.Holidays.AnyAsync(h => h.Date == request.Date, cancellationToken))
        {
            throw new DomainException("holiday.exists", $"{request.Date:yyyy-MM-dd} is already a holiday.");
        }

        var holiday = new Holiday(request.Date, request.Name);
        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record(AuditEntities.Holiday, holiday.Id.ToString(CultureInfo.InvariantCulture), "HolidayAdded", new { holiday.Date, holiday.Name });
        await _db.SaveChangesAsync(cancellationToken);
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name);
    }

    /// <summary>Removes a holiday. Dispute windows already opened keep the end date computed when they opened.</summary>
    public async Task RemoveHolidayAsync(int id, CancellationToken cancellationToken)
    {
        var holiday = await _db.Holidays.SingleOrDefaultAsync(h => h.Id == id, cancellationToken)
            ?? throw new NotFoundException("Holiday", id);
        _db.Holidays.Remove(holiday);
        _audit.Record(AuditEntities.Holiday, id.ToString(CultureInfo.InvariantCulture), "HolidayRemoved", new { holiday.Date, holiday.Name });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static PolicySnapshot ToSnapshot(PolicyDto p) => new(
        p.StandardHoursPerMonth, p.DailyHours, p.DeductionCapPercent, p.DefaultWarrantyDays, p.DisputeWindowDays, p.QaMissDeductible, p.QaMissSharePercent, p.RequireEmployeeAcknowledgement);

    private static PolicyDto ToDto(PolicySnapshot p) => new(
        p.StandardHoursPerMonth, p.DailyHours, p.DeductionCapPercent, p.DefaultWarrantyDays, p.DisputeWindowDays, p.QaMissDeductible, p.QaMissSharePercent, p.RequireEmployeeAcknowledgement);

    private static IEnumerable<(string Key, string Value)> Values(PolicyDto p)
    {
        yield return (PolicySetting.StandardHoursPerMonth, Number(p.StandardHoursPerMonth));
        yield return (PolicySetting.DailyHours, Number(p.DailyHours));
        yield return (PolicySetting.DeductionCapPercent, Number(p.DeductionCapPercent));
        yield return (PolicySetting.DefaultWarrantyDays, p.DefaultWarrantyDays.ToString(CultureInfo.InvariantCulture));
        yield return (PolicySetting.DisputeWindowDays, p.DisputeWindowDays.ToString(CultureInfo.InvariantCulture));
        yield return (PolicySetting.QaMissDeductible, p.QaMissDeductible ? "true" : "false");
        yield return (PolicySetting.QaMissSharePercent, Number(p.QaMissSharePercent));
        yield return (PolicySetting.RequireEmployeeAcknowledgement, p.RequireEmployeeAcknowledgement ? "true" : "false");
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
