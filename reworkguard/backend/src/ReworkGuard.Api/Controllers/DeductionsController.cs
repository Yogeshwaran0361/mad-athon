using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

/// <summary>
/// Deduction lines and their lifecycle: Draft → Notified → (Disputed → Resolved) → Approved → Locked → Applied,
/// plus Cancelled and Reversed. Employees see only their own lines; team leads see their team's.
/// </summary>
[ApiController]
[Route($"{ApiRoutes.V1}/deductions")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class DeductionsController : ControllerBase
{
    private readonly DeductionPreviewService _preview;
    private readonly DeductionService _deductions;

    public DeductionsController(DeductionPreviewService preview, DeductionService deductions)
    {
        _preview = preview;
        _deductions = deductions;
    }

    /// <summary>
    /// What an employee's payroll for <paramref name="periodMonth"/> deducts: the finalized run's figures once the month
    /// is locked, otherwise a live estimate from approved lines, with pending lines listed separately. Defaults to the
    /// first open month. Employees see themselves, team leads their team, Managers/Finance/Admin anyone.
    /// </summary>
    [HttpGet("preview")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<DeductionPreviewDto> Preview([FromQuery] int employeeId, [FromQuery] string? periodMonth, CancellationToken cancellationToken) =>
        _preview.PreviewAsync(employeeId, periodMonth, cancellationToken);

    /// <summary>Lists deduction lines visible to the caller, newest first.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Everyone)]
    public Task<PagedResult<DeductionDto>> List(
        [FromQuery] int? employeeId,
        [FromQuery] DeductionStatus? status,
        [FromQuery] string? applyMonth,
        [FromQuery] int? bugId,
        [FromQuery] bool? openDispute,
        [FromQuery] PagingQuery paging,
        CancellationToken cancellationToken) =>
        _deductions.ListAsync(new DeductionListQuery(employeeId, status, applyMonth, bugId, openDispute), paging.ToPageRequest(), cancellationToken);

    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType<DeductionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<DeductionDto> Get(int id, CancellationToken cancellationToken) => _deductions.GetAsync(id, cancellationToken);

    /// <summary>Notifies employees of Draft lines and opens their dispute windows. All lines or none.</summary>
    [HttpPost("notify")]
    [Authorize(Roles = Roles.DeductionDeciders)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<DeductionDto>> Notify(DeductionIdsRequest request, CancellationToken cancellationToken) =>
        _deductions.NotifyAsync(request, cancellationToken);

    /// <summary>Final approval: lines whose window has closed without a dispute, or whose dispute is resolved. All or none.</summary>
    [HttpPost("approve")]
    [Authorize(Roles = Roles.DeductionDeciders)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<DeductionDto>> Approve(DeductionIdsRequest request, CancellationToken cancellationToken) =>
        _deductions.ApproveAsync(request, cancellationToken);

    /// <summary>The employee disputes their own notified line, once, while the window is open.</summary>
    [HttpPost("{id:int}/dispute")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DeductionDto> Dispute(int id, RaiseDisputeRequest request, CancellationToken cancellationToken) =>
        _deductions.RaiseDisputeAsync(id, request, cancellationToken);

    /// <summary>Upheld (cancel, or reduce with new hours/share) or Rejected (line stands). Justification required.</summary>
    [HttpPost("{id:int}/dispute/resolve")]
    [Authorize(Roles = Roles.DeductionDeciders)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DeductionDto> ResolveDispute(int id, ResolveDisputeRequest request, CancellationToken cancellationToken) =>
        _deductions.ResolveDisputeAsync(id, request, cancellationToken);

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = Roles.DeductionDeciders)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DeductionDto> Cancel(int id, ReasonRequest request, CancellationToken cancellationToken) =>
        _deductions.CancelAsync(id, request, cancellationToken);

    /// <summary>The employee confirms they have seen the line.</summary>
    [HttpPost("{id:int}/acknowledge")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DeductionDto> Acknowledge(int id, CancellationToken cancellationToken) => _deductions.AcknowledgeAsync(id, cancellationToken);

    /// <summary>Raises a negative line that reverses an applied line in the next payroll run.</summary>
    [HttpPost("{id:int}/compensate")]
    [Authorize(Roles = Roles.DeductionDeciders)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DeductionDto> Compensate(int id, ReasonRequest request, CancellationToken cancellationToken) =>
        _deductions.CompensateAsync(id, request, cancellationToken);

    /// <summary>Re-runs the ledger for one bug (idempotent), e.g. after correcting a pay rate.</summary>
    [HttpPost("~/" + ApiRoutes.V1 + "/bugs/{bugId:int}/deductions/recalculate")]
    [Authorize(Roles = Roles.DeductionDeciders)]
    public Task<IReadOnlyList<DeductionDto>> Recalculate(int bugId, CancellationToken cancellationToken) =>
        _deductions.RecalculateBugAsync(bugId, cancellationToken);
}
