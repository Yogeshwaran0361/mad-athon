using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Admin;

namespace ReworkGuard.Api.Controllers;

/// <summary>Creates projects and modules. Listing projects and changing warranty live on <see cref="AdminController"/>.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/admin/projects")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class ProjectAdminController : ControllerBase
{
    private readonly ProjectAdminService _projects;

    public ProjectAdminController(ProjectAdminService projects)
    {
        _projects = projects;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectWarrantyDto>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var created = await _projects.CreateProjectAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Modules), new { id = created.ProjectId }, created);
    }

    [HttpGet("{id:int}/modules")]
    [Authorize(Roles = Roles.Everyone)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ModuleDto>> Modules(int id, CancellationToken cancellationToken) => _projects.ModulesAsync(id, cancellationToken);

    [HttpPost("{id:int}/modules")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModuleDto>> AddModule(int id, CreateModuleRequest request, CancellationToken cancellationToken)
    {
        var created = await _projects.AddModuleAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(Modules), new { id }, created);
    }
}
