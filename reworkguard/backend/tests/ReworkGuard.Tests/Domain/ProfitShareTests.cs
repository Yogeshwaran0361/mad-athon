using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Tests.Domain;

public sealed class ProfitShareTests
{
    private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Worked_example_two_contributors_60_and_40_hours_at_400()
    {
        var result = ProfitShareCalculator.Calculate(100_000m, 1.5m, 20m, [new(1, 60m, 400m), new(2, 40m, 400m)]);

        result.LabourCost.Should().Be(60_000m);
        result.Profit.Should().Be(40_000m);
        result.Pool.Should().Be(8_000m);
        result.Lines.Select(l => (l.EmployeeId, l.SharePercent, l.Amount)).Should().Equal((1, 60.00m, 4_800m), (2, 40.00m, 3_200m));
    }

    [Theory]
    [InlineData(50_000)]
    [InlineData(60_000)]
    public void A_loss_or_break_even_gives_an_empty_pool_and_no_awards(decimal received)
    {
        var result = ProfitShareCalculator.Calculate(received, 1.5m, 20m, [new(1, 60m, 400m), new(2, 40m, 400m)]);

        result.Profit.Should().Be(received - 60_000m);
        result.Pool.Should().Be(0m);
        result.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData(new[] { 1.0, 1.0, 1.0 }, 100.00)]
    [InlineData(new[] { 7.0, 5.0, 3.0 }, 1000.01)]
    [InlineData(new[] { 2.5, 2.5, 5.0 }, 33.33)]
    [InlineData(new[] { 1.0, 2.0, 3.0 }, 0.05)]
    public void Shares_total_exactly_100_and_amounts_exactly_the_pool_for_awkward_splits(double[] hours, double pool)
    {
        // Choose received so the pool equals the given figure: zero labour cost, 100% share.
        var labour = hours.Select((h, i) => new ProfitShareLabour(i + 1, (decimal)h, 0m)).ToList();

        var result = ProfitShareCalculator.Calculate((decimal)pool, 1.5m, 100m, labour);

        result.Pool.Should().Be((decimal)pool);
        result.Lines.Sum(l => l.SharePercent).Should().Be(100.00m);
        result.Lines.Sum(l => l.Amount).Should().Be((decimal)pool);
        result.Lines.Should().OnlyContain(l => decimal.Round(l.SharePercent, 2) == l.SharePercent && decimal.Round(l.Amount, 2) == l.Amount);
        result.Lines[0].Hours.Should().Be(result.Lines.Max(l => l.Hours), "the remainder goes to the largest share");
    }

    [Fact]
    public void A_three_way_even_split_gives_the_remainder_to_the_first_largest_share()
    {
        var result = ProfitShareCalculator.Calculate(100m, 1m, 100m, [new(3, 1m, 0m), new(1, 1m, 0m), new(2, 1m, 0m)]);

        result.Lines.Select(l => (l.EmployeeId, l.SharePercent, l.Amount)).Should().Equal((1, 33.34m, 33.34m), (2, 33.33m, 33.33m), (3, 33.33m, 33.33m));
    }

    [Fact]
    public void Several_logs_of_one_employee_are_summed_and_valued_at_their_own_rates()
    {
        var result = ProfitShareCalculator.Calculate(10_000m, 1.5m, 20m, [new(1, 4m, 300m), new(1, 6m, 350m), new(2, 10m, 250m)]);

        result.LabourCost.Should().Be((4m * 300m + 6m * 350m + 10m * 250m) * 1.5m);
        result.Lines.Select(l => (l.EmployeeId, l.Hours, l.SharePercent)).Should().Equal((1, 10m, 50m), (2, 10m, 50m));
    }

    [Fact]
    public void Nobody_approves_their_own_award_and_a_rejection_needs_a_reason()
    {
        var award = new IncentiveAward(1, employeeId: 5, 10m, 100m, 500m, new YearMonth(2026, 4), Now);

        award.Invoking(a => a.Approve(5, Now)).Should().Throw<DomainForbiddenException>().Which.Code.Should().Be("award.self_approval");
        award.Invoking(a => a.Reject(6, " ", Now)).Should().Throw<DomainException>().Which.Code.Should().Be("award.reason_required");
        award.Approve(6, Now);
        award.Status.Should().Be(IncentiveAwardStatus.Approved);
        award.Invoking(a => a.Approve(7, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("award.already_decided");
    }

    [Fact]
    public void Earnings_are_a_separate_line_and_never_raise_the_deduction_cap()
    {
        var employee = new PayrollEmployeeInput(1, true, 10_000m, [new PayrollLineInput(9, new YearMonth(2026, 4), new DateOnly(2026, 3, 2), 5_000m, null)], [])
        {
            Earnings = [new PayrollEarningInput(77, 4_800m)],
        };

        var result = PayrollRunCalculator.Calculate(new PayrollRunInput(new YearMonth(2026, 4), 20m, [employee])).Employees.Single();

        result.CapAmount.Should().Be(2_000m, "the cap is 20% of the 10,000 salary base, not of salary plus earnings");
        result.Applied.Should().Be(2_000m);
        result.Earnings.Should().Be(4_800m);
        result.NetPay.Should().Be(10_000m + 4_800m - 2_000m);
    }

    [Fact]
    public void Earnings_change_the_inputs_hash()
    {
        var plain = new PayrollEmployeeInput(1, true, 10_000m, [], []);
        var withAward = plain with { Earnings = [new PayrollEarningInput(77, 100m)] };

        PayrollInputsHash.Compute(new PayrollRunInput(new YearMonth(2026, 4), 20m, [plain]))
            .Should().NotBe(PayrollInputsHash.Compute(new PayrollRunInput(new YearMonth(2026, 4), 20m, [withAward])));
    }
}
