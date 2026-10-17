using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Admin;

/// <summary>
/// User, role and pay administration. Pay changes are effective-dated entries in the rate history, never overwrites,
/// so corrective work is always valued at the rate in force when it was done. Salaries never appear in audit details
/// beyond the change itself, and passwords never do.
/// </summary>
public sealed class EmployeeAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IWorkCalendar _calendar;
    private readonly IAuditWriter _audit;
    private readonly IPasswordHasher _hasher;
    private readonly IValidator<CreateEmployeeRequest> _createValidator;
    private readonly IValidator<UpdateEmployeeRequest> _updateValidator;
    private readonly IValidator<ChangePayRequest> _payValidator;
    private readonly IValidator<ResetPasswordRequest> _passwordValidator;

    public EmployeeAdminService(
        IApplicationDbContext db,
        ICurrentUser user,
        IClock clock,
        IWorkCalendar calendar,
        IAuditWriter audit,
        IPasswordHasher hasher,
        IValidator<CreateEmployeeRequest> createValidator,
        IValidator<UpdateEmployeeRequest> updateValidator,
        IValidator<ChangePayRequest> payValidator,
        IValidator<ResetPasswordRequest> passwordValidator)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _calendar = calendar;
        _audit = audit;
        _hasher = hasher;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _payValidator = payValidator;
        _passwordValidator = passwordValidator;
    }

    public async Task<PagedResult<EmployeeAdminDto>> ListAsync(string? search, bool includeInactive, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.Employees.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => e.Name.Contains(term) || e.Email.Contains(term) || e.Code.Contains(term));
        }

        return await query.OrderBy(e => e.Name).Select(e => new EmployeeAdminDto(
                e.Id, e.Code, e.Name, e.Email, e.Role, e.ManagerId, e.IsActive, e.PayMode, e.MonthlyGross, e.DailyRate, e.HourlyRate))
            .ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<EmployeeAdminDto> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var email = request.Email.Trim().ToLowerInvariant();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Employees.AnyAsync(e => e.Email == email || e.Code == code, cancellationToken))
        {
            throw new DomainException("employee.duplicate", "An employee with that email or code already exists.");
        }

        await EnsureManagerAsync(request.ManagerId, null, cancellationToken);
        var employee = new Employee(
            code, request.Name.Trim(), email, request.Role, request.PayMode, request.MonthlyGross, request.DailyRate, request.HourlyRate, request.ManagerId, request.PayEffectiveFrom);
        employee.SetPasswordHash(_hasher.Hash(request.Password));
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(cancellationToken);

        Record(employee.Id, "Created", new { employee.Code, employee.Role, employee.ManagerId, employee.PayMode, request.PayEffectiveFrom });
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(employee);
    }

    public async Task<EmployeeAdminDto> UpdateAsync(int id, UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var employee = await LoadAsync(id, cancellationToken);
        if (id == _user.EmployeeId && (!request.IsActive || request.Role != employee.Role))
        {
            throw new DomainException("employee.self_change", "You cannot deactivate yourself or change your own role.");
        }

        await EnsureManagerAsync(request.ManagerId, id, cancellationToken);
        var before = new { employee.Name, employee.Role, employee.ManagerId, employee.IsActive };
        employee.Rename(request.Name);
        employee.ChangeRole(request.Role, request.ManagerId);
        if (request.IsActive)
        {
            employee.Activate();
        }
        else
        {
            employee.Deactivate();
        }

        Record(id, "Updated", new { Before = before, After = new { employee.Name, employee.Role, employee.ManagerId, employee.IsActive } });
        if (!employee.IsActive || employee.Role != before.Role)
        {
            // Access tokens carry the role and expire on their own; refresh tokens must not mint new ones.
            await RevokeSessionsAsync(id, employee.IsActive ? "role changed" : "deactivated", cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(employee);
    }

    public async Task<IReadOnlyList<RateHistoryDto>> PayHistoryAsync(int id, CancellationToken cancellationToken)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            throw new NotFoundException("Employee", id);
        }

        return await _db.EmployeeRateHistory.AsNoTracking()
            .Where(r => r.EmployeeId == id)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new RateHistoryDto(r.Id, r.EffectiveFrom, r.PayMode, r.MonthlyGross, r.DailyRate, r.HourlyRate, r.RecordedByEmployeeId, r.RecordedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Adds an effective-dated pay entry. Lines already drafted keep their snapshot; a manager can re-run the ledger for a
    /// bug to re-value open lines after a backdated correction.
    /// </summary>
    public async Task<IReadOnlyList<RateHistoryDto>> ChangePayAsync(int id, ChangePayRequest request, CancellationToken cancellationToken)
    {
        await _payValidator.ValidateAndThrowAsync(request, cancellationToken);
        var employee = await _db.Employees.Include(e => e.RateHistory).SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("Employee", id);
        var entry = employee.ChangePay(request.PayMode, request.MonthlyGross, request.DailyRate, request.HourlyRate, request.EffectiveFrom, _calendar.Today, _user.EmployeeId, _clock.UtcNow);
        Record(id, "PayChanged", new { entry.EffectiveFrom, entry.PayMode, entry.MonthlyGross, entry.DailyRate, entry.HourlyRate });
        await _db.SaveChangesAsync(cancellationToken);
        return await PayHistoryAsync(id, cancellationToken);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _passwordValidator.ValidateAndThrowAsync(request, cancellationToken);
        var employee = await LoadAsync(id, cancellationToken);
        employee.SetPasswordHash(_hasher.Hash(request.Password));
        Record(id, "PasswordReset", new { ResetBy = _user.EmployeeId });
        await RevokeSessionsAsync(id, "password reset", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeSessionsAsync(int employeeId, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        foreach (var token in await _db.RefreshTokens.Where(t => t.EmployeeId == employeeId && t.RevokedAtUtc == null).ToListAsync(cancellationToken))
        {
            token.Revoke(reason, now);
        }
    }

    private async Task EnsureManagerAsync(int? managerId, int? employeeId, CancellationToken cancellationToken)
    {
        if (managerId is null)
        {
            return;
        }

        if (managerId == employeeId)
        {
            throw new DomainException("employee.self_manager", "An employee cannot report to themselves.");
        }

        if (!await _db.Employees.AnyAsync(e => e.Id == managerId && e.IsActive, cancellationToken))
        {
            throw new NotFoundException("Employee", managerId);
        }
    }

    private async Task<Employee> LoadAsync(int id, CancellationToken cancellationToken) =>
        await _db.Employees.SingleOrDefaultAsync(e => e.Id == id, cancellationToken) ?? throw new NotFoundException("Employee", id);

    private void Record(int employeeId, string action, object details) =>
        _audit.Record(AuditEntities.Employee, employeeId.ToString(CultureInfo.InvariantCulture), action, details);

    private static EmployeeAdminDto ToDto(Employee e) =>
        new(e.Id, e.Code, e.Name, e.Email, e.Role, e.ManagerId, e.IsActive, e.PayMode, e.MonthlyGross, e.DailyRate, e.HourlyRate);
}
