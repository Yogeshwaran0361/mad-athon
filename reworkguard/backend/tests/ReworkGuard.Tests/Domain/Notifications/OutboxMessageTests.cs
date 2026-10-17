using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Tests.Domain.Notifications;

public sealed class OutboxMessageTests
{
    private static readonly DateTime T0 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private static OutboxMessage NewMessage() =>
        new(new Notification(1, NotificationType.BugReported, "Title", "Body", "/tasks/1", "Bug", "1", T0), NotificationChannel.Email, T0);

    [Fact]
    public void A_new_message_is_pending_and_due_immediately()
    {
        var m = NewMessage();
        m.Status.Should().Be(OutboxStatus.Pending);
        m.Attempts.Should().Be(0);
        m.NextAttemptAtUtc.Should().Be(T0);
    }

    [Fact]
    public void Claiming_counts_the_attempt_and_leases_the_message()
    {
        var m = NewMessage();
        m.Claim(T0);
        m.Attempts.Should().Be(1);
        m.NextAttemptAtUtc.Should().Be(T0 + OutboxMessage.Lease);
    }

    [Fact]
    public void Failures_back_off_1_2_4_8_minutes_then_dead_letter_on_the_fifth()
    {
        var m = NewMessage();
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            m.Claim(T0);
            m.MarkFailed("smtp down", T0);
            delays.Add(m.NextAttemptAtUtc - T0);
        }

        delays.Should().Equal(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8));
        m.Status.Should().Be(OutboxStatus.Pending);

        m.Claim(T0);
        m.MarkFailed("smtp down", T0);
        m.Status.Should().Be(OutboxStatus.DeadLettered);
        m.ProcessedAtUtc.Should().Be(T0);
        m.LastError.Should().Be("smtp down");
    }

    [Fact]
    public void Long_errors_are_truncated()
    {
        var m = NewMessage();
        m.Claim(T0);
        m.MarkFailed(new string('x', 5000), T0);
        m.LastError.Should().HaveLength(OutboxMessage.MaxErrorLength);
    }

    [Fact]
    public void Sent_and_skipped_are_final()
    {
        var sent = NewMessage();
        sent.Claim(T0);
        sent.MarkSent(T0);
        sent.Status.Should().Be(OutboxStatus.Sent);
        sent.Invoking(m => m.MarkFailed("late", T0)).Should().Throw<DomainException>().Which.Code.Should().Be("outbox.not_pending");

        var skipped = NewMessage();
        skipped.Skip("Recipient is inactive.", T0);
        skipped.Status.Should().Be(OutboxStatus.Skipped);
        skipped.Invoking(m => m.Claim(T0)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Only_dead_lettered_messages_can_be_requeued_and_they_get_fresh_attempts()
    {
        var m = NewMessage();
        m.Invoking(x => x.Requeue(T0)).Should().Throw<DomainException>().Which.Code.Should().Be("outbox.not_dead_lettered");
        for (var i = 0; i < OutboxMessage.MaxAttempts; i++)
        {
            m.Claim(T0);
            m.MarkFailed("down", T0);
        }

        var later = T0.AddHours(1);
        m.Requeue(later);
        m.Status.Should().Be(OutboxStatus.Pending);
        m.Attempts.Should().Be(0);
        m.NextAttemptAtUtc.Should().Be(later);
        m.ProcessedAtUtc.Should().BeNull();
    }
}
