using System.Globalization;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Payroll;

/// <summary>
/// Independent checks run before a run is finalized. Each one recomputes or cross-checks the stored figures from a
/// different angle; any failure stops finalization.
/// </summary>
public static class PayrollReconciliation
{
    public static IReadOnlyList<ReconciliationCheckDto> Check(
        PayrollRun run,
        PayrollRunResult recomputed,
        IReadOnlyCollection<int> availableLineIds,
        decimal previousCarriedForward,
        decimal carriedInNow)
    {
        var stored = run.Employees.ToDictionary(e => e.EmployeeId);
        var checks = new List<ReconciliationCheckDto>();

        var mismatched = recomputed.Employees
            .Where(r => !stored.TryGetValue(r.EmployeeId, out var s) || s.Applied != r.Applied || s.CarriedForward != r.CarriedForward
                        || s.Refunds != r.Refunds || s.SettledManually != r.SettledManually || s.CapAmount != r.CapAmount
                        || s.Earnings != r.Earnings)
            .Select(r => r.EmployeeId)
            .Concat(stored.Keys.Except(recomputed.Employees.Select(r => r.EmployeeId)))
            .ToList();
        checks.Add(Result("Recomputed figures match", mismatched.Count == 0, mismatched.Count == 0
            ? $"{stored.Count} employees recomputed from current inputs."
            : $"Employees differ: {Ids(mismatched)}."));

        var unbalanced = run.Employees.SelectMany(e => e.Items)
            .Where(i => i.Kind == PayrollItemKind.Refund
                ? -i.Due != -i.Applied + i.CancelledByCompensation
                : i.Due != i.Applied + i.CarriedForward + i.SettledManually + i.CancelledByCompensation)
            .Select(i => i.DeductionId)
            .ToList();
        checks.Add(Result("Every line is fully accounted for", unbalanced.Count == 0, unbalanced.Count == 0
            ? "Due = applied + carried forward + settled manually + cancelled, for every line."
            : $"Lines out of balance: {Ids(unbalanced)}."));

        var overCap = run.Employees.Where(e => e.Applied > e.CapAmount).Select(e => e.EmployeeId).ToList();
        checks.Add(Result("No deduction exceeds the cap", overCap.Count == 0, overCap.Count == 0
            ? $"Cap {run.CapPercent.ToString("0.##", CultureInfo.InvariantCulture)}% of the salary base respected for everyone."
            : $"Over the cap: {Ids(overCap)}."));

        var employeesOff = run.Employees
            .Where(e => e.Applied != e.Items.Where(i => i.Kind != PayrollItemKind.Refund).Sum(i => i.Applied)
                        || e.CarriedForward != e.Items.Sum(i => i.CarriedForward)
                        || e.Refunds != e.Items.Where(i => i.Kind == PayrollItemKind.Refund).Sum(i => i.Applied))
            .Select(e => e.EmployeeId)
            .ToList();
        var totalsMatch = run.TotalApplied == run.Employees.Sum(e => e.Applied) && run.TotalCarriedForward == run.Employees.Sum(e => e.CarriedForward)
                          && run.TotalRefunds == run.Employees.Sum(e => e.Refunds) && run.TotalSettledManually == run.Employees.Sum(e => e.SettledManually);
        checks.Add(Result("Totals add up", employeesOff.Count == 0 && totalsMatch, employeesOff.Count == 0 && totalsMatch
            ? "Run totals equal the sum of employees, and employee totals equal the sum of their lines."
            : $"Totals differ{(employeesOff.Count > 0 ? $" for employees {Ids(employeesOff)}" : " at run level")}."));

        var runLines = run.Employees.SelectMany(e => e.Items).Where(i => i.Kind != PayrollItemKind.CarriedIn).Select(i => i.DeductionId).ToList();
        var taken = runLines.Except(availableLineIds).ToList();
        checks.Add(Result("Lines are still approved and unclaimed", taken.Count == 0, taken.Count == 0
            ? $"{runLines.Count} lines are Approved and in no other finalized run."
            : $"No longer available: {Ids(taken)}."));

        checks.Add(Result("Carry-forward continues from the previous month", previousCarriedForward == carriedInNow,
            string.Create(CultureInfo.InvariantCulture, $"Previous month carried forward {previousCarriedForward:0.00}; this run carries in {carriedInNow:0.00}.")));

        return checks;
    }

    private static ReconciliationCheckDto Result(string name, bool passed, string detail) => new(name, passed, detail);

    private static string Ids(IEnumerable<int> ids) => string.Join(", ", ids.Take(20));
}
