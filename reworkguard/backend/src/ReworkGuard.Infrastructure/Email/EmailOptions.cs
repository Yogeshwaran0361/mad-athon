using System.ComponentModel.DataAnnotations;

namespace ReworkGuard.Infrastructure.Email;

public enum EmailProvider
{
    /// <summary>Writes a one-line record to the log instead of sending. Default for tests and bare local runs.</summary>
    Log,

    /// <summary>Plain SMTP, e.g. MailHog in docker compose.</summary>
    Smtp,

    /// <summary>Amazon SES (API v2). Credentials come from the default AWS chain (task role), never from configuration.</summary>
    Ses,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Log;

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@reworkguard.local";

    [Required]
    public string FromName { get; set; } = "ReworkGuard";

    public SmtpOptions Smtp { get; set; } = new();

    public SesOptions Ses { get; set; } = new();

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "localhost";

        [Range(1, 65535)]
        public int Port { get; set; } = 1025;

        public bool EnableSsl { get; set; }

        /// <summary>Optional. Supply through user-secrets or an environment variable, never appsettings.</summary>
        public string? Username { get; set; }

        public string? Password { get; set; }

        [Range(1, 300)]
        public int TimeoutSeconds { get; set; } = 30;
    }

    public sealed class SesOptions
    {
        /// <summary>AWS region of the SES identity, e.g. ap-south-1. Empty uses the SDK's default resolution.</summary>
        public string? Region { get; set; }

        /// <summary>Optional SES configuration set for bounce/complaint tracking.</summary>
        public string? ConfigurationSetName { get; set; }
    }
}
