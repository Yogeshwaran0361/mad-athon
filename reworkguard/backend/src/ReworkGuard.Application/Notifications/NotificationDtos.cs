using FluentValidation;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Notifications;

public sealed record NotificationDto(
    long Id,
    NotificationType Type,
    string Title,
    string Body,
    string? Link,
    string? EntityType,
    string? EntityId,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

public sealed record UnreadCountDto(int Unread);

public sealed record NotificationPreferenceDto(NotificationType Type, bool EmailEnabled, bool InAppEnabled, bool EmailMandatory);

public sealed record NotificationPreferenceChange(NotificationType Type, bool EmailEnabled);

public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceChange> Preferences);

public sealed record OutboxMessageDto(
    long Id,
    long NotificationId,
    NotificationChannel Channel,
    OutboxStatus Status,
    int Attempts,
    int RecipientEmployeeId,
    NotificationType Type,
    DateTime CreatedAtUtc,
    DateTime NextAttemptAtUtc,
    DateTime? ProcessedAtUtc,
    string? LastError);

public sealed record OutboxRunResult(int Sent, int Skipped, int Retrying, int DeadLettered);

internal sealed class UpdateNotificationPreferencesRequestValidator : AbstractValidator<UpdateNotificationPreferencesRequest>
{
    public UpdateNotificationPreferencesRequestValidator()
    {
        RuleFor(r => r.Preferences).NotNull().NotEmpty();
        RuleForEach(r => r.Preferences).ChildRules(p => p.RuleFor(x => x.Type).IsInEnum());
        RuleFor(r => r.Preferences)
            .Must(p => p.Select(x => x.Type).Distinct().Count() == p.Count)
            .When(r => r.Preferences is not null)
            .WithMessage("Each notification type may appear only once.");
    }
}
