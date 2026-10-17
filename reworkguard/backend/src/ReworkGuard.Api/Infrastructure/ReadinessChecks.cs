using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ReworkGuard.Infrastructure.Persistence;

namespace ReworkGuard.Api.Infrastructure;

/// <summary>Ready when the database answers.</summary>
internal sealed class DatabaseReadinessCheck : IHealthCheck
{
    private readonly ApplicationDbContext _db;

    public DatabaseReadinessCheck(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await _db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database unreachable.");
}

/// <summary>Ready when at least one job server has sent a heartbeat in the last two minutes.</summary>
internal sealed class JobServerReadinessCheck : IHealthCheck
{
    private static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(2);
    private readonly JobStorage _storage;

    public JobServerReadinessCheck(JobStorage storage)
    {
        _storage = storage;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var servers = _storage.GetMonitoringApi().Servers();
        var alive = servers.Any(s => s.Heartbeat is { } beat && DateTime.UtcNow - beat < MaxHeartbeatAge);
        return Task.FromResult(alive ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded("No job server heartbeat; background jobs are not running."));
    }
}
