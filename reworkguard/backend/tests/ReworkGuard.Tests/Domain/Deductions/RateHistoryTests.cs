using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;
using ReworkGuard.Domain.Payroll;
using ReworkGuard.Domain.Policies;

namespace ReworkGuard.Tests.Domain.Deductions;

public sealed class RateHistoryTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 3, 10);

    private static Employee Priya() =>
        new("E1", "Priya", "priya@test.local", Role.Employee, PayMode.Monthly, 48000m, null, null, managerId: null, payEffectiveFrom: new DateOnly(2025, 1, 1));

    [Fact]
    public void A_new_employee_has_one_history_entry_from_their_start_pay()
    {
        var employee = Priya();

        employee.RateHistory.Should().ContainSingle();
        RateHistory.EffectiveOn(employee.RateHistory, new DateOnly(2025, 1, 1)).PayProfile.MonthlyGross.Should().Be(48000m);
    }

    [Fact]
    public void Work_is_valued_at_the_rate_in_force_on_its_work_date()
    {
        var employee = Priya();
        employee.ChangePay(PayMode.Monthly, 64000m, null, null, new DateOnly(2026, 3, 1), Today, recordedByEmployeeId: 99, Now);

        RateHistory.EffectiveOn(employee.RateHistory, new DateOnly(2026, 2, 28)).PayProfile.MonthlyGross.Should().Be(48000m);
        RateHistory.EffectiveOn(employee.RateHistory, new DateOnly(2026, 3, 1)).PayProfile.MonthlyGross.Should().Be(64000m);
        employee.MonthlyGross.Should().Be(64000m, "the entry in force today is shown on the employee");
    }

    [Fact]
    public void A_future_dated_change_does_not_alter_current_pay_yet()
    {
        var employee = Priya();
        employee.ChangePay(PayMode.Hourly, null, null, 400m, new DateOnly(2026, 6, 1), Today, 99, Now);

        employee.PayMode.Should().Be(PayMode.Monthly);
        employee.MonthlyGross.Should().Be(48000m);
        RateHistory.EffectiveOn(employee.RateHistory, new DateOnly(2026, 6, 1)).PayProfile.Should().Be(new PayProfile(PayMode.Hourly, null, null, 400m));
    }

    [Fact]
    public void Pay_mode_changes_drop_the_rates_of_the_other_modes()
    {
        var employee = Priya();
        employee.ChangePay(PayMode.Daily, 99999m, 2400m, 1m, new DateOnly(2026, 1, 1), Today, 99, Now);

        employee.PayProfile.Should().Be(new PayProfile(PayMode.Daily, null, 2400m, null));
    }

    [Fact]
    public void Two_entries_cannot_share_an_effective_date()
    {
        var employee = Priya();
        var act = () => employee.ChangePay(PayMode.Monthly, 50000m, null, null, new DateOnly(2025, 1, 1), Today, 99, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("employee.rate_exists");
    }

    [Fact]
    public void Work_before_the_first_entry_has_no_rate()
    {
        var act = () => RateHistory.EffectiveOn(Priya().RateHistory, new DateOnly(2024, 12, 31));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("payroll.no_rate");
    }

    [Theory]
    [InlineData(PayMode.Monthly, null)]
    [InlineData(PayMode.Monthly, 0.0)]
    [InlineData(PayMode.Monthly, 100.001)]
    public void Pay_must_be_positive_with_two_decimals(PayMode mode, double? amount)
    {
        var act = () => Priya().ChangePay(mode, (decimal?)amount, null, null, new DateOnly(2026, 1, 1), Today, 99, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("employee.pay_missing");
    }

    [Fact]
    public void Role_and_activity_changes_are_recorded_on_the_employee()
    {
        var employee = Priya();

        employee.ChangeRole(Role.TeamLead, managerId: 3);
        employee.Deactivate();

        employee.Role.Should().Be(Role.TeamLead);
        employee.ManagerId.Should().Be(3);
        employee.IsActive.Should().BeFalse();
        employee.Activate();
        employee.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(RootCause.OwnDefect, 75.00, 75.00)]
    [InlineData(RootCause.QaMiss, 75.00, 37.50)]
    [InlineData(RootCause.QaMiss, 33.33, 16.67)] // 16.665 rounds away from zero
    public void Qa_miss_lines_deduct_a_configured_fraction_of_the_share(RootCause cause, double share, double expected)
    {
        var policy = new PolicySnapshot(160m, 8m, 20m, 45, 3, qaMissDeductible: true, qaMissSharePercent: 50m, requireEmployeeAcknowledgement: false);

        DeductibilityPolicy.EffectiveSharePercent((decimal)share, cause, policy).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(0, 3, 50, "policy.invalid_hours")]
    [InlineData(160, 0, 50, "policy.invalid_dispute_window")]
    [InlineData(160, 31, 50, "policy.invalid_dispute_window")]
    [InlineData(160, 3, 100.5, "policy.invalid_qa_share")]
    [InlineData(160, 3, -1, "policy.invalid_qa_share")]
    public void Policy_values_are_validated(double hours, int window, double qaShare, string code)
    {
        var act = () => new PolicySnapshot((decimal)hours, 8m, 20m, 45, window, false, (decimal)qaShare, false);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void Warranty_days_are_bounded()
    {
        var act = () => new PolicySnapshot(160m, 8m, 20m, -1, 3, false, 50m, false);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("policy.invalid_warranty");
    }
}
