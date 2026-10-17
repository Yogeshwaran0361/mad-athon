using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Employees;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Api.Controllers;

[ApiController]
[Route($"{ApiRoutes.V1}/employees")]
public sealed class EmployeesController : ControllerBase
{
    private readonly EmployeeQueryService _employees;

    public EmployeesController(EmployeeQueryService employees)
    {
        _employees = employees;
    }

    [HttpGet]
    [Authorize(Roles = Roles.People)]
    public Task<PagedResult<EmployeeDto>> List([FromQuery] Role? role, [FromQuery] PagingQuery paging, CancellationToken cancellationToken) =>
        _employees.ListAsync(role, paging.ToPageRequest(), cancellationToken);
}
