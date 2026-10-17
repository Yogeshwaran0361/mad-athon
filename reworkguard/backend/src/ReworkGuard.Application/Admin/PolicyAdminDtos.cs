using FluentValidation;

namespace ReworkGuard.Application.Admin;

public sealed record PolicyDto(
    decimal StandardHoursPerMonth,
    decimal DailyHours,
    decimal DeductionCapPercent,
    int DefaultWarrantyDays,
    int DisputeWindowDays,
    bool QaMissDeductible,
    decimal QaMissSharePercent,
    bool RequireEmployeeAcknowledgement);

public sealed record ProjectWarrantyDto(int ProjectId, string ProjectName, int WarrantyDays);

public sealed record UpdateWarrantyRequest(int WarrantyDays);

public sealed record HolidayDto(int Id, DateOnly Date, string Name);

public sealed record CreateHolidayRequest(DateOnly Date, string Name);

internal sealed class PolicyDtoValidator : AbstractValidator<PolicyDto>
{
    public PolicyDtoValidator()
    {
        RuleFor(x => x.StandardHoursPerMonth).InclusiveBetween(1, 744).PrecisionScale(6, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.DailyHours).InclusiveBetween(1, 24).PrecisionScale(4, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.DeductionCapPercent).InclusiveBetween(0, 100).PrecisionScale(5, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.DefaultWarrantyDays).InclusiveBetween(0, 3650);
        RuleFor(x => x.DisputeWindowDays).InclusiveBetween(1, 30);
        RuleFor(x => x.QaMissSharePercent).InclusiveBetween(0, 100).PrecisionScale(5, 2, ignoreTrailingZeros: true);
    }
}

internal sealed class UpdateWarrantyRequestValidator : AbstractValidator<UpdateWarrantyRequest>
{
    public UpdateWarrantyRequestValidator()
    {
        RuleFor(x => x.WarrantyDays).InclusiveBetween(0, 3650);
    }
}

internal sealed class CreateHolidayRequestValidator : AbstractValidator<CreateHolidayRequest>
{
    public CreateHolidayRequestValidator()
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly));
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
