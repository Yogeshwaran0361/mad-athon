using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Infrastructure.Email;

/// <summary>Sends through an SMTP server (MailHog locally). Sends a multipart message with text and HTML parts.</summary>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = _options.Smtp;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = smtp.TimeoutSeconds * 1000,
        };
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
        }

        using var mail = Build(message, _options);
        await client.SendMailAsync(mail, cancellationToken);
    }

    internal static MailMessage Build(EmailMessage message, EmailOptions options)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = message.Subject,
            SubjectEncoding = System.Text.Encoding.UTF8,
            Body = message.TextBody,
            BodyEncoding = System.Text.Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.To.Add(new MailAddress(message.ToAddress, message.ToName));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html));
        return mail;
    }
}
