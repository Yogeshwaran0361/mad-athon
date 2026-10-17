using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Jobs;
using ReworkGuard.Infrastructure.Jobs;

namespace ReworkGuard.Api.Controllers;

/// <summary>Scheduled background jobs: list, run now, and open the Hangfire dashboard. Administrators only.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/admin/jobs")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class JobsController : ControllerBase
{
    private readonly IJobMonitor _jobs;
    private readonly DashboardSession _dashboard;
    private readonly ICurrentUser _user;

    public JobsController(IJobMonitor jobs, DashboardSession dashboard, ICurrentUser user)
    {
        _jobs = jobs;
        _dashboard = dashboard;
        _user = user;
    }

    [HttpGet]
    [Authorize(Roles = Roles.Administrators)]
    public IReadOnlyList<ScheduledJobDto> List() => _jobs.ListRecurring();

    /// <summary>Queues the job to run now, outside its schedule.</summary>
    [HttpPost("{id}/trigger")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Trigger(string id) => Accepted(new { jobId = _jobs.Trigger(id) });

    /// <summary>
    /// Sets a 30-minute, HttpOnly cookie that opens the dashboard at /hangfire in a browser tab. Each dashboard request
    /// re-checks that the holder is still an active administrator.
    /// </summary>
    [HttpPost("dashboard-session")]
    [Authorize(Roles = Roles.Administrators)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult DashboardSessionCookie()
    {
        Response.Cookies.Append(DashboardSession.CookieName, _dashboard.Issue(_user.EmployeeId), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/hangfire",
            MaxAge = DashboardSession.Lifetime,
            IsEssential = true,
        });
        return NoContent();
    }
}
