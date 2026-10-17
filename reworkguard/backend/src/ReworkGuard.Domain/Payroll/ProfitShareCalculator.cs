using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Payroll;

/// <summary>One approved Enhancement log valued at the employee's hourly rate on its work date.</summary>
public sealed record ProfitShareLabour(int EmployeeId, decimal Hours, decimal HourlyRate);

public sealed record ProfitShareLine(int EmployeeId, decimal Hours, decimal SharePercent, decimal Amount);

public sealed record ProfitShareResult(decimal LabourCost, decimal Profit, decimal Pool, IReadOnlyList<ProfitShareLine> Lines);

/// <summary>
/// Profit share of a closed change request. Labour cost = Σ hours × hourly rate × overhead factor; profit = received −
/// labour cost; pool = max(0, profit) × percent. Each contributor's share is their hours ÷ total hours, rounded to two
/// decimals half away from zero; shares are made to total exactly 100.00 and amounts exactly the pool, the rounding
/// remainder going to the largest share. A loss gives an empty pool and nobody is penalised.
/// </summary>
public static class ProfitShareCalculator
{
    public static ProfitShareResult Calculate(decimal receivedExTax, decimal overheadFactor, decimal profitSharePercent, IReadOnlyCollection<ProfitShareLabour> labour)
    {
        if (overheadFactor < 0 || profitSharePercent is < 0 or > 100)
        {
            throw new DomainException("policy.invalid_profit_share", "The overhead factor must be non-negative and the profit share between 0 and 100 percent.");
        }

        var labourCost = Precision.Round2(labour.Sum(l => l.Hours * l.HourlyRate) * overheadFactor);
        var profit = receivedExTax - labourCost;
        var pool = Precision.Round2(Math.Max(0m, profit) * profitSharePercent / 100m);

        var hours = labour.GroupBy(l => l.EmployeeId).Select(g => (EmployeeId: g.Key, Hours: g.Sum(l => l.Hours))).Where(h => h.Hours > 0).ToList();
        var totalHours = hours.Sum(h => h.Hours);
        if (pool == 0 || totalHours == 0)
        {
            return new ProfitShareResult(labourCost, profit, pool, []);
        }

        // Largest share first (ties: lowest employee id) so it absorbs the rounding remainders.
        var ordered = hours.OrderByDescending(h => h.Hours).ThenBy(h => h.EmployeeId).ToList();
        var shares = ordered.Select(h => Precision.Round2(h.Hours / totalHours * 100m)).ToArray();
        shares[0] += 100m - shares.Sum();
        var amounts = shares.Select(s => Precision.Round2(pool * s / 100m)).ToArray();
        amounts[0] += pool - amounts.Sum();

        return new ProfitShareResult(
            labourCost,
            profit,
            pool,
            ordered.Select((h, i) => new ProfitShareLine(h.EmployeeId, h.Hours, shares[i], amounts[i])).ToList());
    }
}
