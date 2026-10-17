using ReworkGuard.Application.Notifications;

namespace ReworkGuard.Application.Jobs;

/// <summary>Scheduled wrapper around <see cref="OutboxProcessor"/>: drains due messages in batches.</summary>
public sealed class OutboxDeliveryJob
{
    public const int MaxBatchesPerRun = 20;

    private readonly OutboxProcessor _processor;

    public OutboxDeliveryJob(OutboxProcessor processor)
    {
        _processor = processor;
    }

    public Task<OutboxRunResult> RunAsync(CancellationToken cancellationToken) => _processor.ProcessDueAsync(MaxBatchesPerRun, cancellationToken);
}
