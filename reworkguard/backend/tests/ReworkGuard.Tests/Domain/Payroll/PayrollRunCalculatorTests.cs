using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Tests.Domain.Payroll;

/// <summary>Every expected figure here is worked out by hand in the comment beside it.</summary>
public sealed class PayrollRunCalculatorTests
{
    private static readonly YearMonth April = YearMonth.Parse("2026-04");
    private static readonly YearMonth March = YearMonth.Parse("2026-03");
    private static readonly YearMonth February = YearMonth.Parse("2026-02");

    [Fact]
    public void Under_the_cap_everything_is_applied()
    {
        // Cap 20% of 48,000 = 9,600. Lines 900 + 450 = 1,350 all fit.
        var e = One(48000m, lines: [Line(1, 2, 900m), Line(2, 5, 450m)]);

        e.CapAmount.Should().Be(9600.00m);
        e.CurrentDue.Should().Be(1350.00m);
        e.Applied.Should().Be(1350.00m);
        e.CarriedForward.Should().Be(0m);
        e.NetAdjustment.Should().Be(-1350.00m);
    }

    [Fact]
    public void Over_the_cap_lines_fill_it_in_work_date_order_and_the_rest_carries()
    {
        // Cap 20% of 10,000 = 2,000. 2 Mar 1,500 fits; 5 Mar 800 → 500 applied, 300 carried; 9 Mar 300 → all carried.
        var e = One(10000m, lines: [Line(3, 9, 300m), Line(1, 2, 1500m), Line(2, 5, 800m)]);

        e.Items.Select(i => (i.DeductionId, i.Applied, i.CarriedForward)).Should().Equal((1, 1500m, 0m), (2, 500m, 300m), (3, 0m, 300m));
        e.Applied.Should().Be(2000.00m);
        e.CarriedForward.Should().Be(600.00m);
    }

    [Fact]
    public void Exactly_at_the_cap_nothing_is_carried()
    {
        var e = One(10000m, lines: [Line(1, 2, 1200m), Line(2, 3, 800m)]);
        e.Applied.Should().Be(2000m);
        e.CarriedForward.Should().Be(0m);
    }

    [Fact]
    public void Carried_amounts_are_recovered_first_oldest_first()
    {
        // Cap 2,000. Carry from Feb 400 and Mar 300 go first (700), then the April line 1,500 gets the remaining 1,300.
        var e = One(10000m, lines: [Line(10, 2, 1500m)], carried: [Carry(5, March, 300m), Carry(4, February, 400m)]);

        e.Items.Select(i => (i.DeductionId, i.Kind, i.Applied, i.CarriedForward)).Should().Equal(
            (4, PayrollItemKind.CarriedIn, 400m, 0m),
            (5, PayrollItemKind.CarriedIn, 300m, 0m),
            (10, PayrollItemKind.Current, 1300m, 200m));
        e.CarriedIn.Should().Be(700m);
        e.CarriedForward.Should().Be(200m);
    }

    [Fact]
    public void A_backlog_drains_over_several_months_without_losing_or_inventing_money()
    {
        // 5,000 due with a 2,000 cap: 2,000 + 2,000 + 1,000 over three months.
        var may = YearMonth.Parse("2026-05");
        var june = YearMonth.Parse("2026-06");
        var first = One(10000m, lines: [Line(1, 2, 2500m), Line(2, 3, 2500m)]);
        var second = One(10000m, month: may, carried: Carried(first, April));
        var third = One(10000m, month: june, carried: Carried(second, may));

        new[] { first.Applied, second.Applied, third.Applied }.Should().Equal(2000m, 2000m, 1000m);
        third.CarriedForward.Should().Be(0m);
        (first.Applied + second.Applied + third.Applied).Should().Be(5000m);
    }

    [Fact]
    public void Carry_forward_crosses_the_year_end()
    {
        var december = YearMonth.Parse("2025-12");
        var january = YearMonth.Parse("2026-01");
        var dec = One(10000m, month: december, lines: [new PayrollLineInput(1, december, new DateOnly(2025, 11, 20), 2600m, null)]);
        var jan = One(10000m, month: january, carried: Carried(dec, december));

        dec.CarriedForward.Should().Be(600m);
        jan.Items.Single().Should().BeEquivalentTo(new { DeductionId = 1, Kind = PayrollItemKind.CarriedIn, OriginalApplyMonth = december, Applied = 600m, CarriedForward = 0m });
    }

    [Fact]
    public void A_zero_cap_or_zero_base_carries_everything()
    {
        One(10000m, cap: 0m, lines: [Line(1, 2, 100m)]).CarriedForward.Should().Be(100m);
        One(0m, lines: [Line(1, 2, 100m)]).CarriedForward.Should().Be(100m);
    }

    [Fact]
    public void The_cap_amount_is_rounded_to_cents()
    {
        // 20% of 33,333.33 = 6,666.666 → 6,666.67.
        One(33333.33m).CapAmount.Should().Be(6666.67m);
    }

    [Fact]
    public void Refunds_are_paid_in_full_and_never_capped()
    {
        // Cap 2,000; deduction 2,000 applied; refund of an earlier applied line 900 paid back on top. Net pay change: -2,000 + 900 = -1,100.
        var e = One(10000m, lines: [Line(1, 2, 2000m), Refund(2, reverses: 99, 900m)]);

        e.Applied.Should().Be(2000m);
        e.Refunds.Should().Be(-900m);
        e.NetAdjustment.Should().Be(-1100m);
        e.Items.Single(i => i.Kind == PayrollItemKind.Refund).Should().BeEquivalentTo(new { Due = -900m, Applied = -900m, CancelledByCompensation = 0m });
    }

    [Fact]
    public void A_refund_first_cancels_what_is_still_being_carried_on_the_reversed_line()
    {
        // Line 7 was 900; 600 was deducted in March and 300 is still carried. Reversing it cancels the 300 and refunds only 600.
        var e = One(10000m, lines: [Refund(8, reverses: 7, 900m)], carried: [Carry(7, March, 300m)]);

        var carriedItem = e.Items.Single(i => i.DeductionId == 7);
        carriedItem.Should().BeEquivalentTo(new { Due = 300m, Applied = 0m, CarriedForward = 0m, CancelledByCompensation = 300m });
        var refund = e.Items.Single(i => i.DeductionId == 8);
        refund.Should().BeEquivalentTo(new { Due = -900m, Applied = -600m, CancelledByCompensation = 300m });
        e.Refunds.Should().Be(-600m);
        e.CancelledByCompensation.Should().Be(300m);
        e.CarriedForward.Should().Be(0m);
    }

    [Fact]
    public void People_who_have_left_are_listed_for_manual_settlement_not_deducted()
    {
        var e = One(48000m, active: false, lines: [Line(1, 2, 900m)], carried: [Carry(4, March, 250m)]);

        e.ManualSettlement.Should().BeTrue();
        e.CapAmount.Should().Be(0m);
        e.Applied.Should().Be(0m);
        e.CarriedForward.Should().Be(0m);
        e.SettledManually.Should().Be(1150m);
    }

    [Fact]
    public void Run_totals_add_up_across_employees()
    {
        var result = PayrollRunCalculator.Calculate(new PayrollRunInput(April, 20m,
        [
            new PayrollEmployeeInput(2, true, 10000m, [Line(1, 2, 2500m)], []),
            new PayrollEmployeeInput(1, true, 48000m, [Line(2, 3, 900m), Refund(3, 50, 100m)], []),
            new PayrollEmployeeInput(3, false, 0m, [Line(4, 4, 70m)], []),
        ]));

        result.Employees.Select(e => e.EmployeeId).Should().Equal(1, 2, 3);
        result.TotalApplied.Should().Be(2900m); // 900 + 2,000
        result.TotalCarriedForward.Should().Be(500m);
        result.TotalRefunds.Should().Be(-100m);
        result.TotalSettledManually.Should().Be(70m);
    }

    [Fact]
    public void Invalid_inputs_are_refused()
    {
        Fails(() => One(-1m), "payroll.negative_base");
        Fails(() => One(1000m, lines: [new PayrollLineInput(1, YearMonth.Parse("2026-05"), new DateOnly(2026, 4, 2), 10m, null)]), "payroll.line_not_due");
        Fails(() => One(1000m, lines: [new PayrollLineInput(1, April, new DateOnly(2026, 3, 2), -10m, null)]), "payroll.invalid_line");
        Fails(() => One(1000m, lines: [new PayrollLineInput(1, April, new DateOnly(2026, 3, 2), 10.005m, null)]), "payroll.invalid_line");
        Fails(() => One(1000m, carried: [Carry(1, April, 10m)]), "payroll.invalid_carry_forward");
        Fails(() => One(1000m, carried: [Carry(1, March, 0m)]), "payroll.invalid_carry_forward");
        Fails(() => One(1000m, lines: [Line(1, 2, 10m)], carried: [Carry(1, March, 5m)]), "payroll.duplicate_line");
        Fails(() => One(1000m, cap: 101m), "policy.invalid_cap");
        Fails(
            () => PayrollRunCalculator.Calculate(new PayrollRunInput(April, 20m, [new PayrollEmployeeInput(1, true, 0m, [], []), new PayrollEmployeeInput(1, true, 0m, [], [])])),
            "payroll.duplicate_employee");
    }

    [Fact]
    public void Input_order_does_not_change_the_result()
    {
        var lines = Enumerable.Range(1, 8).Select(i => Line(i, 1 + (i * 7 % 28), i * 100m)).ToList();

        var forward = One(10000m, lines: lines);
        var reversed = One(10000m, lines: Enumerable.Reverse(lines).ToList());

        reversed.Items.Should().Equal(forward.Items);
    }

    [Fact]
    public void Property_every_line_is_accounted_for_and_the_cap_is_never_exceeded()
    {
        var random = new Random(4242);
        for (var run = 0; run < 1000; run++)
        {
            var lines = Enumerable.Range(1, random.Next(0, 12))
                .Select(i => Line(i, random.Next(1, 29), Math.Round(1m + ((decimal)random.NextDouble() * 3000m), 2)))
                .ToList();
            var carried = Enumerable.Range(100, random.Next(0, 3)).Select(i => Carry(i, February, Math.Round(1m + ((decimal)random.NextDouble() * 2000m), 2))).ToList();
            var refunds = random.Next(0, 3) == 0 && carried.Count > 0
                ? new List<PayrollLineInput> { Refund(500, carried[0].DeductionId, Math.Round(1m + ((decimal)random.NextDouble() * 3000m), 2)) }
                : [];
            var e = One(Math.Round((decimal)random.NextDouble() * 60000m, 2), cap: random.Next(0, 41), active: random.Next(0, 10) > 0, lines: [.. lines, .. refunds], carried: carried);

            e.Applied.Should().BeLessThanOrEqualTo(e.CapAmount, "run {0}", run);
            e.Applied.Should().BeGreaterThanOrEqualTo(0m);
            foreach (var item in e.Items)
            {
                if (item.Kind == PayrollItemKind.Refund)
                {
                    (-item.Due).Should().Be(-item.Applied + item.CancelledByCompensation, "run {0}", run);
                }
                else
                {
                    item.Due.Should().Be(item.Applied + item.CarriedForward + item.SettledManually + item.CancelledByCompensation, "run {0}", run);
                }
            }

            (e.CarriedIn + e.CurrentDue).Should().Be(e.Applied + e.CarriedForward + e.SettledManually + e.CancelledByCompensation, "run {0}", run);
        }
    }

    private static PayrollLineInput Line(int id, int aprilWorkDay, decimal amount) => new(id, April, new DateOnly(2026, 3, aprilWorkDay), amount, null);

    private static PayrollLineInput Refund(int id, int reverses, decimal amount) => new(id, April, new DateOnly(2026, 3, 1), -amount, reverses);

    private static PayrollCarryInput Carry(int id, YearMonth month, decimal outstanding) => new(id, month, month.FirstDay.AddDays(-20), outstanding);

    private static List<PayrollCarryInput> Carried(PayrollEmployeeResult previous, YearMonth previousMonth) =>
        previous.Items.Where(i => i.CarriedForward > 0).Select(i => new PayrollCarryInput(i.DeductionId, i.OriginalApplyMonth, i.WorkDate, i.CarriedForward)).ToList();

    private static PayrollEmployeeResult One(
        decimal salaryBase,
        decimal cap = 20m,
        bool active = true,
        YearMonth? month = null,
        List<PayrollLineInput>? lines = null,
        List<PayrollCarryInput>? carried = null) =>
        PayrollRunCalculator.Calculate(new PayrollRunInput(month ?? April, cap, [new PayrollEmployeeInput(1, active, salaryBase, lines ?? [], carried ?? [])])).Employees.Single();

    private static void Fails(Action act, string code) => act.Should().Throw<DomainException>().Which.Code.Should().Be(code);
}
