using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Tasks;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

[ApiController]
[Route($"{ApiRoutes.V1}/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly TaskService _tasks;

    public TasksController(TaskService tasks)
    {
        _tasks = tasks;
    }

    [HttpGet]
    [Authorize(Roles = Roles.Everyone)]
    public Task<PagedResult<TaskSummaryDto>> List(
        [FromQuery] WorkTaskStatus? status,
        [FromQuery] int? assigneeId,
        [FromQuery] PagingQuery paging,
        CancellationToken cancellationToken) =>
        _tasks.ListAsync(new TaskListQuery(status, assigneeId), paging.ToPageRequest(), cancellationToken);

    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<TaskDetailDto> Get(int id, CancellationToken cancellationToken) => _tasks.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = Roles.TaskPlanners)]
    [ProducesResponseType<TaskSummaryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TaskSummaryDto>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var created = await _tasks.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Moves a task along its lifecycle. Reopening is not available here: it only happens by reporting a bug.</summary>
    [HttpPost("{id:int}/transitions")]
    [Authorize(Roles = Roles.Delivery)]
    [ProducesResponseType<TaskDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<TaskDetailDto> Transition(int id, TransitionTaskRequest request, CancellationToken cancellationToken) =>
        _tasks.TransitionAsync(id, request, cancellationToken);
}
