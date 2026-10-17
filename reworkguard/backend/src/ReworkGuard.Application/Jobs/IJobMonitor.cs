namespace ReworkGuard.Application.Jobs;

public sealed record ScheduledJobDto(
    string Id,
    string Cron,
    string TimeZone,
    DateTime? NextRunUtc,
    DateTime? LastRunUtc,
    string? LastState,
    string? Error);

/// <summary>Administrator's view of the scheduled jobs. Implemented by the job scheduler in Infrastructure.</summary>
public interface IJobMonitor
{
    IReadOnlyList<ScheduledJobDto> ListRecurring();

    /// <summary>Queues a recurring job to run now and returns the queued job's id.</summary>
    string Trigger(string recurringJobId);
}
