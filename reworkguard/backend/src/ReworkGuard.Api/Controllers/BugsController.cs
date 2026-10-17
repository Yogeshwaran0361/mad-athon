using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Attribution;
using ReworkGuard.Application.Bugs;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

[ApiController]
[Route(ApiRoutes.V1)]
public sealed class BugsController : ControllerBase
{
    private readonly BugService _bugs;
    private readonly DefectReviewService _reviews;
    private readonly AttributionService _attribution;

    public BugsController(BugService bugs, DefectReviewService reviews, AttributionService attribution)
    {
        _bugs = bugs;
        _reviews = reviews;
        _attribution = attribution;
    }

    /// <summary>Reports a defect on a Completed (or Closed, within warranty) task and reopens it.</summary>
    [HttpPost("tasks/{taskId:int}/bugs")]
    [Authorize(Roles = Roles.BugReporters)]
    [ProducesResponseType<BugReportedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BugReportedDto>> Report(int taskId, ReportBugRequest request, CancellationToken cancellationToken)
    {
        var reported = await _bugs.ReportAsync(taskId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = reported.Bug.Id }, reported);
    }

    [HttpGet("bugs")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<PagedResult<BugDto>> List(
        [FromQuery] BugStatus? status,
        [FromQuery] int? taskId,
        [FromQuery] bool? reviewed,
        [FromQuery] PagingQuery paging,
        CancellationToken cancellationToken) =>
        _bugs.ListAsync(new BugListQuery(status, taskId, reviewed), paging.ToPageRequest(), cancellationToken);

    [HttpGet("bugs/{id:int}")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<BugDto> Get(int id, CancellationToken cancellationToken) => _bugs.GetAsync(id, cancellationToken);

    [HttpPost("bugs/{id:int}/verify")]
    [Authorize(Roles = Roles.BugVerifiers)]
    public Task<BugDto> Verify(int id, CancellationToken cancellationToken) => _bugs.VerifyAsync(id, cancellationToken);

    [HttpPost("bugs/{id:int}/close")]
    [Authorize(Roles = Roles.TaskPlanners)]
    public Task<BugDto> Close(int id, CancellationToken cancellationToken) => _bugs.CloseAsync(id, cancellationToken);

    /// <summary>Manager's root-cause verdict. Only an OwnDefect caused by the original delivery inside warranty is deductible.</summary>
    [HttpPost("bugs/{id:int}/review")]
    [Authorize(Roles = Roles.Reviewers)]
    public Task<BugDto> Review(int id, ReviewBugRequest request, CancellationToken cancellationToken) =>
        _reviews.ReviewAsync(id, request, cancellationToken);

    [HttpGet("bugs/{id:int}/attribution")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<AttributionDto> Attribution(int id, CancellationToken cancellationToken) => _attribution.GetAsync(id, cancellationToken);

    [HttpPut("bugs/{id:int}/attribution")]
    [Authorize(Roles = Roles.Reviewers)]
    public Task<AttributionDto> OverrideAttribution(int id, OverrideAttributionRequest request, CancellationToken cancellationToken) =>
        _attribution.OverrideAsync(id, request, cancellationToken);
}
