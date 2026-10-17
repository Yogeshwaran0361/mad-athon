using FluentValidation;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Admin;

public sealed record EmployeeAdminDto(
    int Id,
    string Code,
    string Name,
    string Email,
    Role Role,
    int? ManagerId,
    bool IsActive,
    PayMode PayMode,
    decimal? MonthlyGross,
    decimal? DailyRate,
    decimal? HourlyRate);

public sealed record RateHistoryDto(
    int Id,
    DateOnly EffectiveFrom,
    PayMode PayMode,
    decimal? MonthlyGross,
    decimal? DailyRate,
    decimal? HourlyRate,
    int? RecordedByEmployeeId,
    DateTime? RecordedAtUtc);

public sealed record CreateEmployeeRequest(
    string Code,
    string Name,
    string Email,
    Role Role,
    int? ManagerId,
    PayMode PayMode,
    decimal? MonthlyGross,
    decimal? DailyRate,
    decimal? HourlyRate,
    DateOnly PayEffectiveFrom,
    string Password);

public sealed record UpdateEmployeeRequest(string Name, Role Role, int? ManagerId, bool IsActive);

public sealed record ChangePayRequest(PayMode PayMode, decimal? MonthlyGross, decimal? DailyRate, decimal? HourlyRate, DateOnly EffectiveFrom);

public sealed record ResetPasswordRequest(string Password);

/// <summary>Shared password rules: at least 12 characters with a letter and a digit.</summary>
internal static class PasswordRules
{
    public const int MinimumLength = 12;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinimumLength)
            .MaximumLength(128)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage($"Passwords need at least {MinimumLength} characters, including a letter and a digit.");
}

internal sealed class CreateEmployeeRequestValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.PayMode).IsInEnum();
        RuleFor(x => x.ManagerId).GreaterThan(0).When(x => x.ManagerId is not null);
        RuleFor(x => x.PayEffectiveFrom).NotEqual(default(DateOnly));
        RuleFor(x => x.Password).StrongPassword();
    }
}

internal sealed class UpdateEmployeeRequestValidator : AbstractValidator<UpdateEmployeeRequest>
{
    public UpdateEmployeeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.ManagerId).GreaterThan(0).When(x => x.ManagerId is not null);
    }
}

internal sealed class ChangePayRequestValidator : AbstractValidator<ChangePayRequest>
{
    public ChangePayRequestValidator()
    {
        RuleFor(x => x.PayMode).IsInEnum();
        RuleFor(x => x.EffectiveFrom).NotEqual(default(DateOnly));
        RuleFor(x => x.MonthlyGross).NotNull().GreaterThan(0).When(x => x.PayMode == PayMode.Monthly);
        RuleFor(x => x.DailyRate).NotNull().GreaterThan(0).When(x => x.PayMode == PayMode.Daily);
        RuleFor(x => x.HourlyRate).NotNull().GreaterThan(0).When(x => x.PayMode == PayMode.Hourly);
    }
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Password).StrongPassword();
    }
}
