using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Payroll;

/// <summary>An approved line due in the run. Negative amounts are compensations (refunds) of an earlier applied line.</summary>
public sealed record PayrollLineInput(int DeductionId, YearMonth ApplyMonth, DateOnly WorkDate, decimal Amount, int? ReversesDeductionId);

/// <summary>What is still owed on a line from the previous finalized run.</summary>
public sealed record PayrollCarryInput(int DeductionId, YearMonth OriginalApplyMonth, DateOnly WorkDate, decimal Outstanding);

/// <summary>An approved earning (a change-request profit share) paid in the run. Never netted against deductions.</summary>
public sealed record PayrollEarningInput(int AwardId, decimal Amount);

public sealed record PayrollEmployeeInput(
    int EmployeeId,
    bool IsActive,
    decimal SalaryBase,
    IReadOnlyList<PayrollLineInput> Lines,
    IReadOnlyList<PayrollCarryInput> CarriedIn)
{
    public IReadOnlyList<PayrollEarningInput> Earnings { get; init; } = [];
}

public sealed record PayrollRunInput(YearMonth PeriodMonth, decimal CapPercent, IReadOnlyList<PayrollEmployeeInput> Employees);

/// <summary>
/// One line's outcome. For deductions: Due = Applied + CarriedForward + SettledManually + CancelledByCompensation.
/// For refunds Due is negative, Applied is the (negative) amount paid back, and CancelledByCompensation is the part
/// of the refund used to cancel carry-forward instead: -Due = -Applied + CancelledByCompensation.
/// </summary>
public sealed record PayrollItemResult(
    int DeductionId,
    PayrollItemKind Kind,
    YearMonth OriginalApplyMonth,
    DateOnly WorkDate,
    decimal Due,
    decimal Applied,
    decimal CarriedForward,
    decimal SettledManually,
    decimal CancelledByCompensation);

public sealed record PayrollEmployeeResult(
    int EmployeeId,
    bool ManualSettlement,
    decimal SalaryBase,
    decimal CapAmount,
    decimal CarriedIn,
    decimal CurrentDue,
    decimal Refunds,
    decimal CancelledByCompensation,
    decimal Applied,
    decimal CarriedForward,
    decimal SettledManually,
    IReadOnlyList<PayrollItemResult> Items)
{
    /// <summary>Net change to pay: refunds paid back minus deductions applied (negative means pay goes down).</summary>
    public decimal NetAdjustment => -Refunds - Applied;

    /// <summary>Profit-share earnings paid this run. A separate line: never part of the cap base, never netted.</summary>
    public decimal Earnings { get; init; }

    /// <summary>Net = Gross + Earnings − Applied deductions (+ any refunds).</summary>
    public decimal NetPay => SalaryBase + Earnings + NetAdjustment;
}

public sealed record PayrollRunResult(YearMonth PeriodMonth, decimal CapPercent, IReadOnlyList<PayrollEmployeeResult> Employees)
{
    public decimal TotalApplied => Employees.Sum(e => e.Applied);

    public decimal TotalCarriedForward => Employees.Sum(e => e.CarriedForward);

    public decimal TotalRefunds => Employees.Sum(e => e.Refunds);

    public decimal TotalSettledManually => Employees.Sum(e => e.SettledManually);
}

/// <summary>
/// Pure payroll run engine. Per employee: the cap is CapPercent of the month's salary base; carried-in amounts are
/// recovered first (oldest first), then this run's lines in work-date order, each taking what fits under the cap and
/// carrying the rest forward. Refunds are paid in full and are never capped. A refund first cancels whatever of the
/// line it reverses is still being carried forward, and only the part that was actually deducted is paid back.
/// Employees who have left are not deducted automatically: everything they owe is listed for manual settlement.
/// </summary>
public static class PayrollRunCalculator
{
    public static PayrollRunResult Calculate(PayrollRunInput input)
    {
        if (input.CapPercent is < 0 or > 100)
        {
            throw new DomainException("policy.invalid_cap", "The deduction cap must be between 0 and 100 percent.");
        }

        var duplicate = input.Employees.GroupBy(e => e.EmployeeId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException("payroll.duplicate_employee", $"Employee {duplicate.Key} appears twice in the run.");
        }

        return new PayrollRunResult(
            input.PeriodMonth,
            input.CapPercent,
            input.Employees.OrderBy(e => e.EmployeeId).Select(e => CalculateEmployee(input, e)).ToList());
    }

    private static PayrollEmployeeResult CalculateEmployee(PayrollRunInput run, PayrollEmployeeInput employee)
    {
        Validate(run, employee);

        var refunds = employee.Lines.Where(l => l.Amount < 0).OrderBy(l => l.WorkDate).ThenBy(l => l.DeductionId).ToList();
        var carried = employee.CarriedIn.ToDictionary(c => c.DeductionId, c => c.Outstanding);
        var cancelled = new Dictionary<int, decimal>();
        var items = new List<PayrollItemResult>();

        // Refunds first cancel outstanding carry-forward of the line they reverse.
        foreach (var refund in refunds)
        {
            var toRefund = -refund.Amount;
            if (refund.ReversesDeductionId is { } reversed && carried.TryGetValue(reversed, out var outstanding) && outstanding > 0)
            {
                var cancel = Math.Min(outstanding, toRefund);
                carried[reversed] = outstanding - cancel;
                cancelled[reversed] = cancelled.GetValueOrDefault(reversed) + cancel;
                toRefund -= cancel;
            }

            items.Add(new PayrollItemResult(refund.DeductionId, PayrollItemKind.Refund, refund.ApplyMonth, refund.WorkDate, refund.Amount, -toRefund, 0m, 0m, -refund.Amount - toRefund));
        }

        var capAmount = employee.IsActive ? Precision.Round2(employee.SalaryBase * run.CapPercent / 100m) : 0m;
        var remaining = capAmount;
        var candidates = employee.CarriedIn
            .OrderBy(c => c.OriginalApplyMonth).ThenBy(c => c.WorkDate).ThenBy(c => c.DeductionId)
            .Select(c => (c.DeductionId, Kind: PayrollItemKind.CarriedIn, Month: c.OriginalApplyMonth, c.WorkDate, Due: c.Outstanding, Payable: carried[c.DeductionId]))
            .Concat(employee.Lines.Where(l => l.Amount > 0)
                .OrderBy(l => l.ApplyMonth).ThenBy(l => l.WorkDate).ThenBy(l => l.DeductionId)
                .Select(l => (l.DeductionId, Kind: PayrollItemKind.Current, Month: l.ApplyMonth, l.WorkDate, Due: l.Amount, Payable: l.Amount)));

        foreach (var c in candidates)
        {
            var cancelledHere = c.Kind == PayrollItemKind.CarriedIn ? cancelled.GetValueOrDefault(c.DeductionId) : 0m;
            if (!employee.IsActive)
            {
                items.Add(new PayrollItemResult(c.DeductionId, c.Kind, c.Month, c.WorkDate, c.Due, 0m, 0m, c.Payable, cancelledHere));
                continue;
            }

            var applied = Math.Min(c.Payable, remaining);
            remaining -= applied;
            items.Add(new PayrollItemResult(c.DeductionId, c.Kind, c.Month, c.WorkDate, c.Due, applied, c.Payable - applied, 0m, cancelledHere));
        }

        // The cap above is a share of the salary base only: earnings never raise it.
        var deductions = items.Where(i => i.Kind != PayrollItemKind.Refund).ToList();
        var refundItems = items.Where(i => i.Kind == PayrollItemKind.Refund).ToList();
        return new PayrollEmployeeResult(
            employee.EmployeeId,
            ManualSettlement: !employee.IsActive,
            employee.SalaryBase,
            capAmount,
            CarriedIn: deductions.Where(i => i.Kind == PayrollItemKind.CarriedIn).Sum(i => i.Due),
            CurrentDue: deductions.Where(i => i.Kind == PayrollItemKind.Current).Sum(i => i.Due),
            Refunds: refundItems.Sum(i => i.Applied),
            CancelledByCompensation: cancelled.Values.Sum(),
            Applied: deductions.Sum(i => i.Applied),
            CarriedForward: deductions.Sum(i => i.CarriedForward),
            SettledManually: deductions.Sum(i => i.SettledManually),
            items)
        {
            Earnings = employee.Earnings.Sum(e => e.Amount),
        };
    }

    private static void Validate(PayrollRunInput run, PayrollEmployeeInput employee)
    {
        if (employee.SalaryBase < 0)
        {
            throw new DomainException("payroll.negative_base", $"Employee {employee.EmployeeId}: the salary base cannot be negative.");
        }

        foreach (var line in employee.Lines)
        {
            if (line.Amount == 0 || !Precision.HasAtMostTwoDecimals(line.Amount))
            {
                throw new DomainException("payroll.invalid_line", $"Deduction {line.DeductionId}: amount must be non-zero with at most two decimals.");
            }

            if (line.ApplyMonth > run.PeriodMonth)
            {
                throw new DomainException("payroll.line_not_due", $"Deduction {line.DeductionId} is due in {line.ApplyMonth}, after {run.PeriodMonth}.");
            }

            if (line.Amount < 0 && line.ReversesDeductionId is null)
            {
                throw new DomainException("payroll.invalid_line", $"Deduction {line.DeductionId}: a refund must name the line it reverses.");
            }
        }

        foreach (var carry in employee.CarriedIn)
        {
            if (carry.Outstanding <= 0 || carry.OriginalApplyMonth >= run.PeriodMonth)
            {
                throw new DomainException("payroll.invalid_carry_forward", $"Deduction {carry.DeductionId}: carried-forward amounts must be positive and from an earlier month.");
            }
        }

        foreach (var earning in employee.Earnings)
        {
            if (earning.Amount <= 0 || !Precision.HasAtMostTwoDecimals(earning.Amount))
            {
                throw new DomainException("payroll.invalid_earning", $"Award {earning.AwardId}: an earning must be positive with at most two decimals.");
            }
        }

        if (employee.Earnings.Select(e => e.AwardId).Distinct().Count() != employee.Earnings.Count)
        {
            throw new DomainException("payroll.duplicate_earning", $"Employee {employee.EmployeeId}: an award appears twice.");
        }

        var ids = employee.Lines.Select(l => l.DeductionId).Concat(employee.CarriedIn.Select(c => c.DeductionId)).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            throw new DomainException("payroll.duplicate_line", $"Employee {employee.EmployeeId}: a deduction line appears twice.");
        }
    }
}
