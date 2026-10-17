using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.ProfitShare;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

/// <summary>Profit-share awards from closed change requests. Employees see only their own.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/incentive-awards")]
public sealed class IncentiveAwardsController : ControllerBase
{
    private readonly ProfitShareService _awards;

    public IncentiveAwardsController(ProfitShareService awards)
    {
        _awards = awards;
    }

    [HttpGet]
    [Authorize(Roles = Roles.Everyone)]
    public Task<IReadOnlyList<IncentiveAwardDto>> List([FromQuery] IncentiveAwardStatus? status, CancellationToken cancellationToken) =>
        _awards.ListAsync(status, cancellationToken);

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Manager)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IncentiveAwardDto> Approve(int id, CancellationToken cancellationToken) => _awards.ApproveAsync(id, cancellationToken);

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Manager)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IncentiveAwardDto> Reject(int id, RejectAwardRequest request, CancellationToken cancellationToken) =>
        _awards.RejectAsync(id, request, cancellationToken);
}
