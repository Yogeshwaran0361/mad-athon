using Microsoft.Extensions.DependencyInjection.Extensions;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Controllers;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Api.DevTools;

public sealed record AdvanceClockRequest(double Hours);

public sealed record DevClockDto(DateTime UtcNow, double OffsetHours);

/// <summary>
/// Development-only clock control for demos (scripts/seed-demo). Off unless the environment is Development
/// AND DevTools:ClockControl is true; outside that, nothing is registered and the routes do not exist (404).
/// </summary>
public static partial class DevToolsExtensions
{
    public const string ClockControlKey = "DevTools:ClockControl";
    public const string ClockRoute = $"{ApiRoutes.V1}/dev/clock";

    /// <summary>The longest single step: 60 days.</summary>
    public const double MaxAdvanceHours = 24 * 60;

    public static bool IsClockControlEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue(ClockControlKey, false);

    public static IServiceCollection AddDevTools(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        if (IsClockControlEnabled(environment, configuration))
        {
            services.AddSingleton<DevClock>();
            services.Replace(ServiceDescriptor.Singleton<IClock>(sp => sp.GetRequiredService<DevClock>()));
        }

        return services;
    }

    public static void MapDevTools(this WebApplication app)
    {
        if (!IsClockControlEnabled(app.Environment, app.Configuration))
        {
            return;
        }

        LogClockControlEnabled(app.Logger, ClockRoute);

        var group = app.MapGroup(ClockRoute).RequireAuthorization(policy => policy.RequireRole(Roles.Administrators)).ExcludeFromDescription();
        group.MapGet(string.Empty, (DevClock clock) => Results.Ok(new DevClockDto(clock.UtcNow, clock.Offset.TotalHours)));
        group.MapPost("advance", (AdvanceClockRequest request, DevClock clock) =>
        {
            if (request.Hours <= 0 || request.Hours > MaxAdvanceHours)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"Hours must be greater than 0 and at most {MaxAdvanceHours}.");
            }

            clock.Advance(TimeSpan.FromHours(request.Hours));
            return Results.Ok(new DevClockDto(clock.UtcNow, clock.Offset.TotalHours));
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Development clock control is enabled at /{Route}. Never enable it outside local development.")]
    private static partial void LogClockControlEnabled(ILogger logger, string route);
}
