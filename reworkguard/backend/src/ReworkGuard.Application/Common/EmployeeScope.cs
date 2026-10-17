using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Common;

/// <summary>
/// Whose records the current user may see: Manager, Finance and Admin see everyone; a TeamLead sees themselves
/// and the people who report to them; everyone else sees only themselves.
/// </summary>
public static class EmployeeScope
{
    public static bool SeesEveryone(Role role) => role is Role.Manager or Role.Finance or Role.Admin;

    public static IQueryable<int> VisibleEmployeeIds(IApplicationDbContext db, ICurrentUser user)
    {
        var me = user.EmployeeId;
        IQueryable<Employee> employees = db.Employees;
        return user.Role switch
        {
            _ when SeesEveryone(user.Role) => employees.Select(e => e.Id),
            Role.TeamLead => employees.Where(e => e.Id == me || e.ManagerId == me).Select(e => e.Id),
            _ => employees.Where(e => e.Id == me).Select(e => e.Id),
        };
    }

    /// <summary>Throws <see cref="ForbiddenException"/> unless the current user may see <paramref name="employeeId"/>'s records.</summary>
    public static async Task EnsureVisibleAsync(IApplicationDbContext db, ICurrentUser user, int employeeId, CancellationToken cancellationToken)
    {
        if (SeesEveryone(user.Role) || employeeId == user.EmployeeId)
        {
            return;
        }

        var visible = user.Role == Role.TeamLead
            && await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                db.Employees, e => e.Id == employeeId && e.ManagerId == user.EmployeeId, cancellationToken);
        if (!visible)
        {
            throw new ForbiddenException("You can only see your own records.");
        }
    }
}
