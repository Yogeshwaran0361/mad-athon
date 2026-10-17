using System.Net;
using System.Net.Sockets;
using System.Text;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Infrastructure.Email;

namespace ReworkGuard.Tests.Infrastructure;

public sealed class EmailSenderTests
{
    private static readonly EmailMessage Message = new("priya@example.com", "Priya Monthly", "[ReworkGuard] Deduction notice", "Plain text body", "<p>HTML body</p>");

    [Fact]
    public async Task Smtp_sender_delivers_a_multipart_message_to_the_server()
    {
        using var server = new FakeSmtpServer();
        var options = new EmailOptions { FromAddress = "no-reply@reworkguard.test", FromName = "ReworkGuard", Smtp = { Host = "127.0.0.1", Port = server.Port } };

        await new SmtpEmailSender(Options.Create(options)).SendAsync(Message, CancellationToken.None);

        var transcript = await server.TranscriptAsync();
        transcript.Should().Contain("MAIL FROM:<no-reply@reworkguard.test>").And.Contain("RCPT TO:<priya@example.com>");
        transcript.Should().Contain("Subject: [ReworkGuard] Deduction notice").And.Contain("multipart/alternative").And.Contain("text/html");
    }

    [Fact]
    public async Task Smtp_sender_throws_when_the_server_is_unreachable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var options = new EmailOptions { Smtp = { Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 } };

        var act = () => new SmtpEmailSender(Options.Create(options)).SendAsync(Message, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Ses_request_carries_both_bodies_sender_and_configuration_set()
    {
        var options = new EmailOptions { FromAddress = "no-reply@reworkguard.example", FromName = "Rework \"Guard\"", Ses = { ConfigurationSetName = "rg-events" } };

        var request = SesEmailSender.BuildRequest(Message, options);

        request.FromEmailAddress.Should().Be("\"Rework Guard\" <no-reply@reworkguard.example>");
        request.Destination.ToAddresses.Should().Equal("priya@example.com");
        request.ConfigurationSetName.Should().Be("rg-events");
        request.Content.Simple.Subject.Data.Should().Be(Message.Subject);
        request.Content.Simple.Body.Text.Data.Should().Be("Plain text body");
        request.Content.Simple.Body.Html.Data.Should().Be("<p>HTML body</p>");
    }

    [Fact]
    public async Task Ses_sender_throws_on_a_non_success_response()
    {
        var ses = new FakeSes(HttpStatusCode.InternalServerError);
        var sender = new SesEmailSender(ses.Proxy, Options.Create(new EmailOptions()));

        await sender.Invoking(s => s.SendAsync(Message, CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*500*");

        var ok = new FakeSes(HttpStatusCode.OK);
        await new SesEmailSender(ok.Proxy, Options.Create(new EmailOptions())).SendAsync(Message, CancellationToken.None);
        ok.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("priya@example.com", "p***@example.com")]
    [InlineData("a@example.com", "***@example.com")]
    [InlineData("not-an-address", "***")]
    public void Log_sender_masks_addresses(string address, string masked)
    {
        LogEmailSender.Mask(address).Should().Be(masked);
    }

    [Fact]
    public async Task Log_sender_never_fails()
    {
        await new LogEmailSender(NullLogger<LogEmailSender>.Instance).SendAsync(Message, CancellationToken.None);
    }

    /// <summary>Minimal SMTP server: accepts one message and records the conversation.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task<string> _session;

        public FakeSmtpServer()
        {
            _listener.Start();
            _session = RunAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task<string> TranscriptAsync() => _session.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() => _listener.Stop();

        private async Task<string> RunAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var log = new StringBuilder();
            await writer.WriteLineAsync("220 fake ESMTP");
            var inData = false;
            while (await reader.ReadLineAsync() is { } line)
            {
                log.AppendLine(line);
                if (inData)
                {
                    if (line == ".")
                    {
                        inData = false;
                        await writer.WriteLineAsync("250 queued");
                    }

                    continue;
                }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 fake");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    inData = true;
                    await writer.WriteLineAsync("354 go ahead");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }

            return log.ToString();
        }
    }

    /// <summary>Stands in for the SES client: answers SendEmail with a fixed status code, never touching AWS.</summary>
    private sealed class FakeSes : AmazonSimpleEmailServiceV2Client
    {
        private readonly HttpStatusCode _status;

        public FakeSes(HttpStatusCode status)
            : base(new Amazon.Runtime.AnonymousAWSCredentials(), Amazon.RegionEndpoint.APSouth1)
        {
            _status = status;
        }

        public IAmazonSimpleEmailServiceV2 Proxy => this;

        public List<SendEmailRequest> Requests { get; } = [];

        public override Task<SendEmailResponse> SendEmailAsync(SendEmailRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new SendEmailResponse { HttpStatusCode = _status, MessageId = "m-1" });
        }
    }
}
