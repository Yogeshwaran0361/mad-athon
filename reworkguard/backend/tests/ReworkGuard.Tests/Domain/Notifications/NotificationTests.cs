using ReworkGuard.Application.Notifications;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Notifications;

namespace ReworkGuard.Tests.Domain.Notifications;

public sealed class NotificationTests
{
    private static readonly DateTime T0 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("", "Body", null, "notification.invalid_title")]
    [InlineData("Title", " ", null, "notification.invalid_body")]
    [InlineData("Title", "Body", "https://evil.example/phish", "notification.invalid_link")]
    public void Invalid_notifications_are_refused(string title, string body, string? link, string code)
    {
        var act = () => new Notification(1, NotificationType.BugReported, title, body, link, null, null, T0);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    [Theory]
    [InlineData(0, "INR 0.00")]
    [InlineData(999.5, "INR 999.50")]
    [InlineData(1000, "INR 1,000.00")]
    [InlineData(123456.5, "INR 1,23,456.50")]
    [InlineData(12345678.9, "INR 1,23,45,678.90")]
    [InlineData(-900, "-INR 900.00")]
    public void Money_uses_indian_grouping(double amount, string expected)
    {
        NotificationTemplates.Money((decimal)amount).Should().Be(expected);
    }

    [Fact]
    public void Marking_read_keeps_the_first_read_time()
    {
        var n = new Notification(1, NotificationType.BugReported, "Title", "Body", "/tasks/1", null, null, T0);
        n.IsRead.Should().BeFalse();
        n.MarkRead(T0.AddMinutes(1));
        n.MarkRead(T0.AddMinutes(5));
        n.ReadAtUtc.Should().Be(T0.AddMinutes(1));
        n.IsRead.Should().BeTrue();
    }

    [Theory]
    [InlineData(NotificationType.DeductionNotified, true)]
    [InlineData(NotificationType.DisputeResolved, true)]
    [InlineData(NotificationType.PayslipPublished, true)]
    [InlineData(NotificationType.BugReported, false)]
    [InlineData(NotificationType.DisputeRaised, false)]
    public void Formal_notices_always_go_by_email(NotificationType type, bool mandatory)
    {
        NotificationCatalog.IsEmailMandatory(type).Should().Be(mandatory);
        var turnOff = () => new NotificationPreference(1, type, emailEnabled: false);
        if (mandatory)
        {
            turnOff.Should().Throw<DomainException>().Which.Code.Should().Be("notification.email_mandatory");
        }
        else
        {
            turnOff().EmailEnabled.Should().BeFalse();
        }
    }

    [Fact]
    public void Deduction_notice_states_amount_lines_and_the_ist_window_end()
    {
        // 13 March 2026 18:29:59 UTC = 13 March 23:59 IST.
        var n = NotificationTemplates.DeductionsNotified(7, 2, 123456.5m, new DateTime(2026, 3, 13, 18, 29, 59, DateTimeKind.Utc), 3);
        n.RecipientEmployeeId.Should().Be(7);
        n.Title.Should().Be("Deduction notice: INR 1,23,456.50");
        n.Body.Should().Contain("2 deduction lines totalling INR 1,23,456.50 were raised").And.Contain("3 working day(s)").And.Contain("13 Mar 2026, 23:59 IST");
        n.Link.Should().Be("/my-deductions");
    }

    [Theory]
    [InlineData(DisputeOutcome.Upheld, null, "was cancelled")]
    [InlineData(DisputeOutcome.Upheld, 450.0, "was reduced from INR 900.00 to INR 450.00")]
    [InlineData(DisputeOutcome.Rejected, null, "stands")]
    public void Dispute_outcome_wording(DisputeOutcome outcome, double? newAmount, string expected)
    {
        var n = NotificationTemplates.DisputeResolved(1, 5, outcome, 900m, (decimal?)newAmount, "Checked the logs");
        n.Body.Should().Contain(expected).And.EndWith("Reason: Checked the logs");
        n.Title.Should().Be(outcome == DisputeOutcome.Upheld ? "Your dispute was upheld" : "Your dispute was rejected");
    }

    [Fact]
    public void Every_template_fits_the_entity_limits()
    {
        var longText = new string('a', 5000);
        var all = new[]
        {
            NotificationTemplates.DeductionsNotified(1, 1, 1m, T0, 3),
            NotificationTemplates.DisputeWindowClosing(1, 1, 1m, T0),
            NotificationTemplates.DisputeRaised(1, 1, "Priya", 1m),
            NotificationTemplates.DisputeResolved(1, 1, DisputeOutcome.Rejected, 1m, null, longText),
            NotificationTemplates.DeductionsApproved(1, 3, 1m),
            NotificationTemplates.DeductionCancelled(1, 1, 1m, longText),
            NotificationTemplates.CompensationRaised(1, 1, -1m, "2026-04"),
            NotificationTemplates.BugReported(1, 1, 1, longText, false),
            NotificationTemplates.TimeLogRejected(1, 1, 1, new DateOnly(2026, 3, 9), 2.5m, longText),
        };

        foreach (var r in all)
        {
            var build = () => new Notification(r.RecipientEmployeeId, r.Type, r.Title, r.Body, r.Link, r.EntityType, r.EntityId, T0);
            build.Should().NotThrow(r.Type.ToString());
        }
    }

    [Fact]
    public void Emails_encode_html_and_link_back_into_the_app()
    {
        var n = new Notification(1, NotificationType.BugReported, "Bug <b>", "Body with <script>alert(1)</script>", "/tasks/9", null, null, T0);

        var email = EmailRenderer.Render(n, "priya@example.com", "Priya & Co", "https://app.example.com/");

        email.Subject.Should().Be("[ReworkGuard] Bug <b>");
        email.ToAddress.Should().Be("priya@example.com");
        email.TextBody.Should().Contain("https://app.example.com/tasks/9").And.Contain("https://app.example.com/notifications/preferences");
        email.HtmlBody.Should().Contain("&lt;script&gt;").And.NotContain("<script>").And.Contain("Priya &amp; Co").And.Contain("href=\"https://app.example.com/tasks/9\"");
    }

    [Fact]
    public void Formal_notice_emails_say_they_cannot_be_turned_off()
    {
        var n = new Notification(1, NotificationType.DeductionNotified, "Deduction notice", "Body", "/my-deductions", null, null, T0);
        EmailRenderer.Render(n, "a@b.c", "A", "http://localhost").TextBody.Should().Contain("formal notice").And.NotContain("turn these emails off");
    }
}
