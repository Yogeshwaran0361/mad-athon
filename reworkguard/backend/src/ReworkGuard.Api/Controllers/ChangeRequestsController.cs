using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.ChangeRequests;
using ReworkGuard.Application.ProfitShare;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

/// <summary>Customer enhancement requests: paid new work, never bugs, reopens or deductions.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/change-requests")]
public sealed class ChangeRequestsController : ControllerBase
{
    private readonly ChangeRequestService _changeRequests;
    private readonly ProfitShareService _profitShare;

    public ChangeRequestsController(ChangeRequestService changeRequests, ProfitShareService profitShare)
    {
        _changeRequests = changeRequests;
        _profitShare = profitShare;
    }

    /// <summary>Revenue, labour cost, profit and pool of a closed request. Never shown to employees.</summary>
    [HttpGet("{id:int}/profit-share")]
    [Authorize(Roles = Roles.PolicyViewers)]
    public Task<ProfitShareSummaryDto> ProfitShare(int id, CancellationToken cancellationToken) => _profitShare.SummaryAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = Roles.ChangeRequestCreators)]
    [ProducesResponseType<ChangeRequestDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ChangeRequestDto>> Create(CreateChangeRequestRequest request, CancellationToken cancellationToken)
    {
        var created = await _changeRequests.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    [Authorize(Roles = Roles.ChangeRequestViewers)]
    public Task<IReadOnlyList<ChangeRequestDto>> List([FromQuery] ChangeRequestStatus? status, CancellationToken cancellationToken) =>
        _changeRequests.ListAsync(status, cancellationToken);

    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.ChangeRequestViewers)]
    public Task<ChangeRequestDto> Get(int id, CancellationToken cancellationToken) => _changeRequests.GetAsync(id, cancellationToken);

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Manager)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ChangeRequestDto> Approve(int id, ApproveChangeRequestRequest request, CancellationToken cancellationToken) =>
        _changeRequests.ApproveAsync(id, request, cancellationToken);

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Manager)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ChangeRequestDto> Reject(int id, RejectChangeRequestRequest request, CancellationToken cancellationToken) =>
        _changeRequests.RejectAsync(id, request, cancellationToken);

    [HttpPost("{id:int}/start")]
    [Authorize(Roles = Roles.TaskPlanners)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ChangeRequestDto> Start(int id, CancellationToken cancellationToken) => _changeRequests.StartAsync(id, cancellationToken);

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = Roles.TaskPlanners)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ChangeRequestDto> Complete(int id, CancellationToken cancellationToken) => _changeRequests.CompleteAsync(id, cancellationToken);

    [HttpPost("{id:int}/close")]
    [Authorize(Roles = Roles.Finance)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ChangeRequestDto> Close(int id, CloseChangeRequestRequest request, CancellationToken cancellationToken) =>
        _changeRequests.CloseAsync(id, request, cancellationToken);
}
