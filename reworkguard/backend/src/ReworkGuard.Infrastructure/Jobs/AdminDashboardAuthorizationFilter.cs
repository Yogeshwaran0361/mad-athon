using Hangfire.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Infrastructure.Jobs;

/// <summary>Lets only active administrators with a valid dashboard session cookie see the Hangfire dashboard.</summary>
public sealed class AdminDashboardAuthorizationFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var session = http.RequestServices.GetRequiredService<DashboardSession>();
        if (!session.TryRead(http.Request.Cookies[DashboardSession.CookieName], out var employeeId))
        {
            return false;
        }

        var db = http.RequestServices.GetRequiredService<IApplicationDbContext>();
        return await db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId && e.IsActive && e.Role == Role.Admin, http.RequestAborted);
    }
}
