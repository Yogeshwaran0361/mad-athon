using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Employees;

public sealed class EmployeeQueryService
{
    private readonly IApplicationDbContext _db;

    public EmployeeQueryService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<EmployeeDto>> ListAsync(Role? role, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.Employees.AsNoTracking().Where(e => e.IsActive);
        if (role is not null)
        {
            query = query.Where(e => e.Role == role);
        }

        return await query
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeDto(e.Id, e.Code, e.Name, e.Email, e.Role, e.PayMode, e.IsActive))
            .ToPagedResultAsync(page, cancellationToken);
    }
}
