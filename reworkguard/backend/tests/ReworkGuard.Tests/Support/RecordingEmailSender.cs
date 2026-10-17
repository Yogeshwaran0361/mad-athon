using System.Collections.Concurrent;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Tests.Support;

/// <summary>Captures emails instead of sending them; can be told to fail to exercise retries.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Exception? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
