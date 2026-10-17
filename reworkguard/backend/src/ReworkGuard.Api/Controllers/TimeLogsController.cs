using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.TimeLogs;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

/// <summary>
/// Time logging and approval. There is intentionally no endpoint to edit a log's work type:
/// corrective hours can never be turned into regular hours.
/// </summary>
[ApiController]
[Route($"{ApiRoutes.V1}/timelogs")]
public sealed class TimeLogsController : ControllerBase
{
    private readonly TimeLogService _timeLogs;

    public TimeLogsController(TimeLogService timeLogs)
    {
        _timeLogs = timeLogs;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Delivery)]
    [ProducesResponseType<TimeLogDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimeLogDto>> Log(LogTimeRequest request, CancellationToken cancellationToken)
    {
        var log = await _timeLogs.LogAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, log);
    }

    /// <summary>Lists time logs. Employees and QA only ever see their own.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Everyone)]
    public Task<PagedResult<TimeLogDto>> List(
        [FromQuery] int? employeeId,
        [FromQuery] int? taskId,
        [FromQuery] ApprovalStatus? approvalStatus,
        [FromQuery] WorkType? workType,
        [FromQuery] PagingQuery paging,
        CancellationToken cancellationToken) =>
        _timeLogs.ListAsync(new TimeLogListQuery(employeeId, taskId, approvalStatus, workType), paging.ToPageRequest(), cancellationToken);

    [HttpGet("approval-queue")]
    [Authorize(Roles = Roles.Approvers)]
    public Task<PagedResult<TimeLogDto>> ApprovalQueue([FromQuery] WorkType? workType, [FromQuery] PagingQuery paging, CancellationToken cancellationToken) =>
        _timeLogs.ApprovalQueueAsync(workType, paging.ToPageRequest(), cancellationToken);

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Approvers)]
    public Task<TimeLogDto> Approve(int id, CancellationToken cancellationToken) => _timeLogs.ApproveAsync(id, cancellationToken);

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Approvers)]
    public Task<TimeLogDto> Reject(int id, RejectTimeLogRequest request, CancellationToken cancellationToken) =>
        _timeLogs.RejectAsync(id, request, cancellationToken);
}
