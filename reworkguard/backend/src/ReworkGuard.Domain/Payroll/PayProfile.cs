using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Payroll;

public sealed record PayProfile(PayMode PayMode, decimal? MonthlyGross, decimal? DailyRate, decimal? HourlyRate);
