using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Tests.Domain.Payroll;

public sealed class PayrollInputsHashTests
{
    private static readonly YearMonth April = YearMonth.Parse("2026-04");

    private static PayrollRunInput Input(decimal amount = 900m, decimal salaryBase = 48000m, decimal cap = 20m, bool reverse = false)
    {
        var employees = new List<PayrollEmployeeInput>
        {
            new(1, true, salaryBase, [new PayrollLineInput(10, April, new DateOnly(2026, 3, 2), amount, null), new PayrollLineInput(11, April, new DateOnly(2026, 3, 3), 50m, null)], []),
            new(2, true, 20000m, [], [new PayrollCarryInput(7, YearMonth.Parse("2026-03"), new DateOnly(2026, 2, 3), 300m)]),
        };
        if (reverse)
        {
            employees.Reverse();
            employees[1] = employees[1] with { Lines = employees[1].Lines.Reverse().ToList() };
        }

        return new PayrollRunInput(April, cap, employees);
    }

    [Fact]
    public void The_hash_is_a_stable_sha256_independent_of_input_order()
    {
        var hash = PayrollInputsHash.Compute(Input());
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        PayrollInputsHash.Compute(Input(reverse: true)).Should().Be(hash);
        PayrollInputsHash.Compute(Input()).Should().Be(hash);
    }

    [Fact]
    public void Any_change_to_an_amount_base_or_cap_changes_the_hash()
    {
        var hash = PayrollInputsHash.Compute(Input());
        PayrollInputsHash.Compute(Input(amount: 900.01m)).Should().NotBe(hash);
        PayrollInputsHash.Compute(Input(salaryBase: 48000.01m)).Should().NotBe(hash);
        PayrollInputsHash.Compute(Input(cap: 25m)).Should().NotBe(hash);
    }
}
