using Hangfire;
using ReworkGuard.Application.Jobs;
using ReworkGuard.Application.Notifications;

namespace ReworkGuard.Infrastructure.Jobs;

/// <summary>
/// The methods Hangfire schedules. Each runs in its own DI scope, with no signed-in user, so audit events are
/// recorded as the system. Overlapping runs are prevented with a distributed lock; the jobs are idempotent, so a
/// failed run simply waits for the next schedule instead of being retried.
/// </summary>
public sealed class JobRunner
{
    private readonly IServiceProvider _services;

    public JobRunner(IServiceProvider services)
    {
        _services = services;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public Task<OutboxRunResult> DeliverOutboxAsync(CancellationToken cancellationToken) =>
        Resolve<OutboxDeliveryJob>().RunAsync(cancellationToken);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public Task<DeductionWindowJob.Result> DeductionWindowsAsync(CancellationToken cancellationToken) =>
        Resolve<DeductionWindowJob>().RunAsync(cancellationToken);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 0)]
    public Task<int> WarrantySweepAsync(CancellationToken cancellationToken) =>
        Resolve<WarrantySweepJob>().RunAsync(cancellationToken);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public Task<ReminderJob.Result> RemindersAsync(CancellationToken cancellationToken) =>
        Resolve<ReminderJob>().RunAsync(cancellationToken);

    private T Resolve<T>()
        where T : notnull => (T)(_services.GetService(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered."));
}
