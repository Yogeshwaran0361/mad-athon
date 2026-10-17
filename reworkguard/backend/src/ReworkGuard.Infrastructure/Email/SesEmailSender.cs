using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Infrastructure.Email;

/// <summary>Sends through Amazon SES v2. Throws on any non-success response so the outbox retries.</summary>
internal sealed class SesEmailSender : IEmailSender
{
    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly EmailOptions _options;

    public SesEmailSender(IAmazonSimpleEmailServiceV2 ses, IOptions<EmailOptions> options)
    {
        _ses = ses;
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var response = await _ses.SendEmailAsync(BuildRequest(message, _options), cancellationToken);
        if ((int)response.HttpStatusCode is < 200 or > 299)
        {
            throw new InvalidOperationException($"SES returned HTTP {(int)response.HttpStatusCode}.");
        }
    }

    internal static SendEmailRequest BuildRequest(EmailMessage message, EmailOptions options) => new()
    {
        FromEmailAddress = $"\"{options.FromName.Replace("\"", string.Empty, StringComparison.Ordinal)}\" <{options.FromAddress}>",
        Destination = new Destination { ToAddresses = [message.ToAddress] },
        ConfigurationSetName = string.IsNullOrWhiteSpace(options.Ses.ConfigurationSetName) ? null : options.Ses.ConfigurationSetName,
        Content = new EmailContent
        {
            Simple = new Message
            {
                Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                Body = new Body
                {
                    Text = new Content { Data = message.TextBody, Charset = "UTF-8" },
                    Html = new Content { Data = message.HtmlBody, Charset = "UTF-8" },
                },
            },
        },
    };
}
