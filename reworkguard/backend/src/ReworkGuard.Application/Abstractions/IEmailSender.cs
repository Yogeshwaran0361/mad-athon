namespace ReworkGuard.Application.Abstractions;

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string TextBody, string HtmlBody);

/// <summary>Sends one email. Throws on failure so the outbox can retry; implementations must not log message bodies.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
