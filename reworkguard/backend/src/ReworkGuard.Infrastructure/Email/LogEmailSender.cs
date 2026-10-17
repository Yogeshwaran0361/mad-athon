using Microsoft.Extensions.Logging;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Infrastructure.Email;

/// <summary>Records that an email would have been sent, without the body and with the address masked.</summary>
internal sealed partial class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;

    public LogEmailSender(ILogger<LogEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogEmail(_logger, Mask(message.ToAddress), message.Subject);
        return Task.CompletedTask;
    }

    internal static string Mask(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at switch
        {
            < 0 => "***",
            <= 1 => "***" + address[at..],
            _ => address[0] + "***" + address[at..],
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email delivery is set to Log; not sending '{Subject}' to {Recipient}")]
    private static partial void LogEmail(ILogger logger, string recipient, string subject);
}
