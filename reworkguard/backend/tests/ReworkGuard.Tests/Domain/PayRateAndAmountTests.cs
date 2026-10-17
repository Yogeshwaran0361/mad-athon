using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Tests.Domain;

public sealed class PayRateAndAmountTests
{
    private static readonly DeductionPolicy Policy = new(standardHoursPerMonth: 160m, dailyHours: 8m, deductionCapPercent: 20m);

    [Theory]
    [InlineData(PayMode.Hourly, 275.00, 275.00)]
    [InlineData(PayMode.Daily, 2400.00, 300.00)]
    [InlineData(PayMode.Monthly, 48000.00, 300.00)]
    [InlineData(PayMode.Monthly, 45555.00, 284.72)] // 284.71875
    [InlineData(PayMode.Daily, 1001.00, 125.13)] // 125.125 rounds away from zero
    [InlineData(PayMode.Hourly, 99.995, 100.00)]
    public void Each_pay_mode_converts_to_an_hourly_rate(PayMode mode, double amount, double expectedRate)
    {
        var value = (decimal)amount;
        var pay = mode switch
        {
            PayMode.Hourly => new PayProfile(mode, null, null, value),
            PayMode.Daily => new PayProfile(mode, null, value, null),
            _ => new PayProfile(mode, value, null, null),
        };

        HourlyRateCalculator.HourlyRate(pay, Policy).Should().Be((decimal)expectedRate);
    }

    [Fact]
    public void Daily_and_monthly_conversions_use_the_policy_hours()
    {
        var policy = new DeductionPolicy(standardHoursPerMonth: 168m, dailyHours: 7.5m, deductionCapPercent: 20m);

        HourlyRateCalculator.HourlyRate(new PayProfile(PayMode.Daily, null, 1500m, null), policy).Should().Be(200.00m);
        HourlyRateCalculator.HourlyRate(new PayProfile(PayMode.Monthly, 50400m, null, null), policy).Should().Be(300.00m);
    }

    [Theory]
    [InlineData(PayMode.Hourly)]
    [InlineData(PayMode.Daily)]
    [InlineData(PayMode.Monthly)]
    public void A_missing_rate_for_the_pay_mode_is_an_error(PayMode mode)
    {
        var act = () => HourlyRateCalculator.HourlyRate(new PayProfile(mode, null, null, null), Policy);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("payroll.pay_missing");
    }

    [Theory]
    [InlineData(1.00, 100.01, 50.00, 50.01)] // 50.005 -> away from zero (banker's would give 50.00)
    [InlineData(0.25, 20.02, 100.00, 5.01)] // 5.005
    [InlineData(1.00, 0.01, 50.00, 0.01)] // 0.005
    [InlineData(3.00, 10.00, 33.33, 10.00)] // 9.999
    [InlineData(1.50, 10.01, 33.33, 5.00)] // 5.0045 -> down
    [InlineData(4.00, 300.00, 75.00, 900.00)]
    public void Line_amounts_round_half_away_from_zero_to_two_decimals(double hours, double rate, double share, double expected)
    {
        DeductionAmount.For((decimal)hours, (decimal)rate, (decimal)share).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(0, 8, 20)]
    [InlineData(160, 0, 20)]
    [InlineData(160, 8, -1)]
    [InlineData(160, 8, 100.01)]
    public void Policies_must_be_sane(double standardHours, double dailyHours, double cap)
    {
        var act = () => new DeductionPolicy((decimal)standardHours, (decimal)dailyHours, (decimal)cap);

        act.Should().Throw<DomainException>();
    }
}
