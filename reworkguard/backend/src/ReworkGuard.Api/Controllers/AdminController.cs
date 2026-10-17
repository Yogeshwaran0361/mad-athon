using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Auth;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Admin;
using ReworkGuard.Application.Common;

namespace ReworkGuard.Api.Controllers;

/// <summary>Policy settings, warranty windows, the holiday calendar, users, roles and pay data. Every change is audited.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/admin")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AdminController : ControllerBase
{
    private readonly PolicyAdminService _policies;
    private readonly EmployeeAdminService _employees;

    public AdminController(PolicyAdminService policies, EmployeeAdminService employees)
    {
        _policies = policies;
        _employees = employees;
    }

    [HttpGet("policies")]
    [Authorize(Roles = Roles.PolicyViewers)]
    public Task<PolicyDto> GetPolicies(CancellationToken cancellationToken) => _policies.GetAsync(cancellationToken);

    /// <summary>Replaces the policy. Takes effect for computations made from now on; finalized runs keep their snapshot.</summary>
    [HttpPut("policies")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PolicyDto> UpdatePolicies(PolicyDto request, CancellationToken cancellationToken) => _policies.UpdateAsync(request, cancellationToken);

    [HttpGet("projects")]
    [Authorize(Roles = Roles.PolicyViewers)]
    public Task<IReadOnlyList<ProjectWarrantyDto>> Projects(CancellationToken cancellationToken) => _policies.ProjectsAsync(cancellationToken);

    /// <summary>Changes a project's warranty window for bugs reported from now on.</summary>
    [HttpPut("projects/{id:int}/warranty")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ProjectWarrantyDto> UpdateWarranty(int id, UpdateWarrantyRequest request, CancellationToken cancellationToken) =>
        _policies.UpdateWarrantyAsync(id, request, cancellationToken);

    [HttpGet("holidays")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<IReadOnlyList<HolidayDto>> Holidays([FromQuery] int? year, CancellationToken cancellationToken) => _policies.HolidaysAsync(year, cancellationToken);

    [HttpPost("holidays")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType<HolidayDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HolidayDto>> AddHoliday(CreateHolidayRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await _policies.AddHolidayAsync(request, cancellationToken));

    [HttpDelete("holidays/{id:int}")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveHoliday(int id, CancellationToken cancellationToken)
    {
        await _policies.RemoveHolidayAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("employees")]
    [Authorize(Roles = Roles.PayViewers)]
    public Task<PagedResult<EmployeeAdminDto>> Employees([FromQuery] string? search, [FromQuery] bool includeInactive, [FromQuery] PagingQuery paging, CancellationToken cancellationToken) =>
        _employees.ListAsync(search, includeInactive, paging.ToPageRequest(), cancellationToken);

    [HttpPost("employees")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType<EmployeeAdminDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmployeeAdminDto>> CreateEmployee(CreateEmployeeRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await _employees.CreateAsync(request, cancellationToken));

    [HttpPut("employees/{id:int}")]
    [Authorize(Roles = Roles.Administrators)]
    public Task<EmployeeAdminDto> UpdateEmployee(int id, UpdateEmployeeRequest request, CancellationToken cancellationToken) =>
        _employees.UpdateAsync(id, request, cancellationToken);

    [HttpGet("employees/{id:int}/pay")]
    [Authorize(Roles = Roles.PayViewers)]
    public Task<IReadOnlyList<RateHistoryDto>> PayHistory(int id, CancellationToken cancellationToken) => _employees.PayHistoryAsync(id, cancellationToken);

    /// <summary>Adds an effective-dated pay entry. Earlier work keeps the rate in force when it was done.</summary>
    [HttpPost("employees/{id:int}/pay")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<RateHistoryDto>> ChangePay(int id, ChangePayRequest request, CancellationToken cancellationToken) =>
        _employees.ChangePayAsync(id, request, cancellationToken);

    /// <summary>Clears a sign-in lockout before it expires.</summary>
    [HttpPost("employees/{id:int}/unlock")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlock(int id, [FromServices] AuthService auth, CancellationToken cancellationToken)
    {
        await auth.UnlockAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("employees/{id:int}/password")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _employees.ResetPasswordAsync(id, request, cancellationToken);
        return NoContent();
    }
}
