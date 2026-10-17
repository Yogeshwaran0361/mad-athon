using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Payroll;

namespace ReworkGuard.Api.Controllers;

/// <summary>
/// Monthly payroll runs: Finance generates a Preview (regenerating supersedes it with a new version), reviews it, a
/// Manager who did neither approves it, then Finance finalizes (locks the month) and locks it (applies the lines).
/// Every step takes the run version the caller saw and fails with <c>payroll.stale</c> if it has moved on.
/// </summary>
[ApiController]
[Route($"{ApiRoutes.V1}/payroll")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class PayrollController : ControllerBase
{
    private readonly PayrollRunService _runs;

    public PayrollController(PayrollRunService runs)
    {
        _runs = runs;
    }

    /// <summary>Generates a new run version for the month; any open run for the month is superseded.</summary>
    [HttpPost("runs")]
    [Authorize(Roles = Roles.PayrollOperators)]
    [ProducesResponseType<PayrollRunDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PayrollRunDetailDto> Generate(GeneratePayrollRunRequest request, CancellationToken cancellationToken) =>
        _runs.GenerateAsync(request, cancellationToken);

    [HttpGet("runs")]
    [Authorize(Roles = Roles.PayrollViewers)]
    public Task<IReadOnlyList<PayrollRunSummaryDto>> List([FromQuery] string? periodMonth, CancellationToken cancellationToken) =>
        _runs.ListAsync(periodMonth, cancellationToken);

    [HttpGet("runs/{id:int}")]
    [Authorize(Roles = Roles.PayrollViewers)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<PayrollRunDetailDto> Get(int id, CancellationToken cancellationToken) => _runs.GetAsync(id, cancellationToken);

    /// <summary>One employee's lines in a run: carried in, current, refunds, and what was applied or carried forward.</summary>
    [HttpGet("runs/{id:int}/employees/{employeeId:int}")]
    [Authorize(Roles = Roles.PayrollViewers)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<PayrollRunEmployeeDetailDto> GetEmployee(int id, int employeeId, CancellationToken cancellationToken) =>
        _runs.GetEmployeeAsync(id, employeeId, cancellationToken);

    [HttpPost("runs/{id:int}/review")]
    [Authorize(Roles = Roles.PayrollOperators)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PayrollRunDetailDto> Review(int id, PayrollRunStepRequest request, CancellationToken cancellationToken) =>
        _runs.ReviewAsync(id, request, cancellationToken);

    /// <summary>Manager approval; the approver cannot be the generator or the reviewer. Records the inputs hash approved.</summary>
    [HttpPost("runs/{id:int}/approve")]
    [Authorize(Roles = Roles.PayrollApprovers)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PayrollRunDetailDto> Approve(int id, PayrollRunStepRequest request, CancellationToken cancellationToken) =>
        _runs.ApproveAsync(id, request, cancellationToken);

    /// <summary>Re-checks inputs and reconciliation, moves the lines to Locked and locks the month.</summary>
    [HttpPost("runs/{id:int}/finalize")]
    [Authorize(Roles = Roles.PayrollOperators)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PayrollRunDetailDto> Finalize(int id, PayrollRunStepRequest request, CancellationToken cancellationToken) =>
        _runs.FinalizeAsync(id, request, cancellationToken);

    /// <summary>Applies the run's lines. Irreversible.</summary>
    [HttpPost("runs/{id:int}/lock")]
    [Authorize(Roles = Roles.PayrollOperators)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<PayrollRunDetailDto> Lock(int id, PayrollRunStepRequest request, CancellationToken cancellationToken) =>
        _runs.LockAsync(id, request, cancellationToken);

    [HttpGet("periods")]
    [Authorize(Roles = Roles.PayrollViewers)]
    public Task<IReadOnlyList<PayrollPeriodDto>> Periods(CancellationToken cancellationToken) => _runs.PeriodsAsync(cancellationToken);
}
