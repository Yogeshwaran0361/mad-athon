using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Notifications;

/// <summary>
/// Delivers due outbox messages: claims a batch under row locks (skipping rows another worker holds), then sends
/// each one outside the lock and records the result. Safe to run on several instances at once; delivery is at
/// least once, so a message can repeat if a worker dies between sending and recording.
/// </summary>
public sealed partial class OutboxProcessor
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IEmailSender _email;
    private readonly IRealtimeNotifier _realtime;
    private readonly NotificationOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IApplicationDbContext db,
        IClock clock,
        IEmailSender email,
        IRealtimeNotifier realtime,
        IOptions<NotificationOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _db = db;
        _clock = clock;
        _email = email;
        _realtime = realtime;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OutboxRunResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(cancellationToken);
        if (claimed.Count == 0)
        {
            return new OutboxRunResult(0, 0, 0, 0);
        }

        var recipientIds = claimed.Select(m => m.Notification.RecipientEmployeeId).Distinct().ToList();
        var recipients = await _db.Employees.AsNoTracking()
            .Where(e => recipientIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Email, e.Name, e.IsActive })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        int sent = 0, skipped = 0, retrying = 0, dead = 0;
        foreach (var message in claimed)
        {
            var recipient = recipients[message.Notification.RecipientEmployeeId];
            if (!recipient.IsActive)
            {
                message.Skip("Recipient is inactive.", _clock.UtcNow);
                skipped++;
            }
            else
            {
                try
                {
                    await DeliverAsync(message, recipient.Email, recipient.Name, cancellationToken);
                    message.MarkSent(_clock.UtcNow);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    message.MarkFailed($"{ex.GetType().Name}: {ex.Message}", _clock.UtcNow);
                    LogDeliveryFailed(_logger, message.Id, message.Channel, message.Attempts, ex.GetType().Name);
                    if (message.Status == OutboxStatus.DeadLettered)
                    {
                        dead++;
                    }
                    else
                    {
                        retrying++;
                    }
                }
            }

            // Record each result as it happens so a crash part-way through does not resend the whole batch.
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new OutboxRunResult(sent, skipped, retrying, dead);
    }

    /// <summary>Drains due messages batch by batch, up to <paramref name="maxBatches"/> batches.</summary>
    public async Task<OutboxRunResult> ProcessDueAsync(int maxBatches, CancellationToken cancellationToken)
    {
        var total = new OutboxRunResult(0, 0, 0, 0);
        for (var i = 0; i < maxBatches; i++)
        {
            var run = await ProcessBatchAsync(cancellationToken);
            total = new OutboxRunResult(total.Sent + run.Sent, total.Skipped + run.Skipped, total.Retrying + run.Retrying, total.DeadLettered + run.DeadLettered);
            if (run.Sent + run.Skipped + run.Retrying + run.DeadLettered < _options.OutboxBatchSize)
            {
                break;
            }
        }

        return total;
    }

    private async Task<List<OutboxMessage>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var now = _clock.UtcNow;
        var ids = await _db.LockDueOutboxMessagesAsync(now, _options.OutboxBatchSize, cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var messages = await _db.OutboxMessages.Include(m => m.Notification).Where(m => ids.Contains(m.Id)).OrderBy(m => m.Id).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.Claim(now);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    private Task DeliverAsync(OutboxMessage message, string email, string name, CancellationToken cancellationToken) => message.Channel switch
    {
        NotificationChannel.Email => _email.SendAsync(EmailRenderer.Render(message.Notification, email, name, _options.AppBaseUrl), cancellationToken),
        NotificationChannel.InApp => _realtime.PushAsync(message.Notification.RecipientEmployeeId, NotificationService.ToDto(message.Notification), cancellationToken),
        _ => throw new InvalidOperationException($"Unknown channel {message.Channel}."),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Channel}) failed on attempt {Attempt}: {ErrorType}")]
    private static partial void LogDeliveryFailed(ILogger logger, long messageId, NotificationChannel channel, int attempt, string errorType);
}
