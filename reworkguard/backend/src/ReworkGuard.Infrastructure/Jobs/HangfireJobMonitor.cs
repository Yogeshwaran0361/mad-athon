using Hangfire;
using Hangfire.Storage;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Jobs;

namespace ReworkGuard.Infrastructure.Jobs;

internal sealed class HangfireJobMonitor : IJobMonitor
{
    private readonly JobStorage _storage;
    private readonly IRecurringJobManagerV2 _manager;

    public HangfireJobMonitor(JobStorage storage, IRecurringJobManagerV2 manager)
    {
        _storage = storage;
        _manager = manager;
    }

    public IReadOnlyList<ScheduledJobDto> ListRecurring()
    {
        using var connection = _storage.GetConnection();
        return connection.GetRecurringJobs()
            .OrderBy(j => j.Id, StringComparer.Ordinal)
            .Select(j => new ScheduledJobDto(j.Id, j.Cron, j.TimeZoneId ?? "UTC", j.NextExecution, j.LastExecution, j.LastJobState, j.Error))
            .ToList();
    }

    public string Trigger(string recurringJobId)
    {
        using (var connection = _storage.GetConnection())
        {
            if (connection.GetRecurringJobs([recurringJobId]).All(j => j.Removed || j.Job is null))
            {
                throw new NotFoundException("Recurring job", recurringJobId);
            }
        }

        return _manager.TriggerJob(recurringJobId) ?? throw new InvalidOperationException($"Recurring job {recurringJobId} could not be queued.");
    }
}

/// <summary>Used when jobs are switched off (tests, HTTP-only instances without storage).</summary>
internal sealed class DisabledJobMonitor : IJobMonitor
{
    public IReadOnlyList<ScheduledJobDto> ListRecurring() => [];

    public string Trigger(string recurringJobId) =>
        throw new Domain.Common.DomainException("jobs.disabled", "Background jobs are switched off on this instance.");
}
