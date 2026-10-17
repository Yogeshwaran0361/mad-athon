using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Api.Realtime;
using Serilog;
using Serilog.Events;
using ReworkGuard.Infrastructure.Jobs;
using ReworkGuard.Infrastructure.Persistence;
using ReworkGuard.Infrastructure.Persistence.Seeding;

namespace ReworkGuard.Api;

public static class WebApplicationExtensions
{
    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.MigrateOnStartup && !options.SeedOnStartup)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        if (options.MigrateOnStartup)
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        }

        if (options.SeedOnStartup)
        {
            await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(CancellationToken.None);
        }
    }

    public static void UseApi(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<ServerTimeMiddleware>();
        app.UseSerilogRequestLogging(options =>
        {
            // Path only: query strings can carry tokens (the SignalR hub's access_token).
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms";
            options.GetLevel = (context, _, exception) => exception is not null || context.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("UserId", context.User.FindFirst("sub")?.Value ?? "anonymous");
                diagnostics.Set("CorrelationId", context.TraceIdentifier);
            };
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (app.Environment.IsProduction())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);

        // The dashboard has its own cookie check, so it sits before the API's bearer authentication and fallback policy.
        var jobs = app.Services.GetRequiredService<IOptions<JobsOptions>>().Value;
        if (jobs.Enabled)
        {
            app.UseHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = [], // replaces the default local-requests-only filter
                AsyncAuthorization = [new AdminDashboardAuthorizationFilter()],
                DashboardTitle = "ReworkGuard jobs",
                DisplayStorageConnectionString = false,
                AppPath = null,
            });
            RecurringJobs.Register(app.Services.GetRequiredService<IRecurringJobManager>(), jobs);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapControllers();
        app.MapHub<NotificationsHub>(NotificationsHub.Path);

        // Liveness: the process answers. Readiness: the database (and job server, when enabled) answer too.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ApiServiceCollectionExtensions.ReadyTag) }).AllowAnonymous();
    }
}
