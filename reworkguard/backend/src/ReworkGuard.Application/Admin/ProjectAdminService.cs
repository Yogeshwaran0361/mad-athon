using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Admin;

public sealed record CreateProjectRequest(string Name, int? WarrantyDays);

public sealed record CreateModuleRequest(string Name);

public sealed record ModuleDto(int Id, int ProjectId, string Name);

internal sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WarrantyDays).InclusiveBetween(0, 3650).When(x => x.WarrantyDays is not null);
    }
}

internal sealed class CreateModuleRequestValidator : AbstractValidator<CreateModuleRequest>
{
    public CreateModuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

/// <summary>Creates projects and their modules (the containers tasks are planned in). Every creation is audited.</summary>
public sealed class ProjectAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly PolicyAdminService _policies;
    private readonly IValidator<CreateProjectRequest> _projectValidator;
    private readonly IValidator<CreateModuleRequest> _moduleValidator;

    public ProjectAdminService(
        IApplicationDbContext db,
        IAuditWriter audit,
        PolicyAdminService policies,
        IValidator<CreateProjectRequest> projectValidator,
        IValidator<CreateModuleRequest> moduleValidator)
    {
        _db = db;
        _audit = audit;
        _policies = policies;
        _projectValidator = projectValidator;
        _moduleValidator = moduleValidator;
    }

    /// <summary>Creates a project; without an explicit warranty it takes the policy's default warranty days.</summary>
    public async Task<ProjectWarrantyDto> CreateProjectAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        await _projectValidator.ValidateAndThrowAsync(request, cancellationToken);
        var warranty = request.WarrantyDays ?? (await _policies.GetAsync(cancellationToken)).DefaultWarrantyDays;
        var project = new Project(request.Name.Trim(), warranty);
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record("Project", project.Id.ToString(CultureInfo.InvariantCulture), "ProjectCreated", new { project.Name, project.WarrantyDays });
        await _db.SaveChangesAsync(cancellationToken);
        return new ProjectWarrantyDto(project.Id, project.Name, project.WarrantyDays);
    }

    public async Task<IReadOnlyList<ModuleDto>> ModulesAsync(int projectId, CancellationToken cancellationToken)
    {
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }

        return await _db.Modules.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Name)
            .Select(m => new ModuleDto(m.Id, m.ProjectId, m.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<ModuleDto> AddModuleAsync(int projectId, CreateModuleRequest request, CancellationToken cancellationToken)
    {
        await _moduleValidator.ValidateAndThrowAsync(request, cancellationToken);
        var project = await _db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project", projectId);
        var module = new Module(project, request.Name.Trim());
        _db.Modules.Add(module);
        await _db.SaveChangesAsync(cancellationToken);
        _audit.Record("Module", module.Id.ToString(CultureInfo.InvariantCulture), "ModuleCreated", new { ProjectId = project.Id, module.Name });
        await _db.SaveChangesAsync(cancellationToken);
        return new ModuleDto(module.Id, project.Id, module.Name);
    }
}
