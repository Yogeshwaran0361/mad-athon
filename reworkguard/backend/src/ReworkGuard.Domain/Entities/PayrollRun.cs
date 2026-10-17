using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// One computed version of a month's payroll deductions. Finance generates it (Preview) and marks it Reviewed;
/// a Manager approves it, fixing the inputs hash; Finance finalizes it (inputs re-checked, lines locked, month
/// locked) and finally locks it (lines applied, payslips published). Generating again supersedes earlier
/// unfinalized versions. The computed figures never change after generation.
/// </summary>
public class PayrollRun : IVersioned
{
    private readonly List<PayrollRunEmployee> _employees = [];

    private PayrollRun()
    {
    }

    public PayrollRun(
        YearMonth periodMonth,
        int runVersion,
        string inputsHash,
        PayrollRunResult result,
        IReadOnlyDictionary<int, (string Code, string Name, PayMode PayMode, string SalaryBasis)> employees,
        int generatedBy,
        DateTime utcNow)
    {
        if (result.PeriodMonth != periodMonth)
        {
            throw new ArgumentException("The result is for a different month.", nameof(result));
        }

        PeriodMonth = periodMonth.ToString();
        RunVersion = runVersion;
        InputsHash = inputsHash;
        CapPercent = result.CapPercent;
        Status = PayrollRunStatus.Preview;
        GeneratedByEmployeeId = generatedBy;
        GeneratedAtUtc = utcNow;
        foreach (var e in result.Employees)
        {
            var info = employees[e.EmployeeId];
            _employees.Add(new PayrollRunEmployee(e.EmployeeId, info.Code, info.Name, info.PayMode, info.SalaryBasis, e));
        }

        EmployeeCount = _employees.Count;
        TotalApplied = result.TotalApplied;
        TotalCarriedForward = result.TotalCarriedForward;
        TotalRefunds = result.TotalRefunds;
        TotalSettledManually = result.TotalSettledManually;
    }

    public int Id { get; private set; }

    public string PeriodMonth { get; private set; } = string.Empty;

    /// <summary>1, 2, 3… per month; each regeneration is a new version.</summary>
    public int RunVersion { get; private set; }

    public PayrollRunStatus Status { get; private set; }

    public string InputsHash { get; private set; } = string.Empty;

    /// <summary>The hash the manager approved; finalization requires the current inputs to hash to this.</summary>
    public string? ApprovedInputsHash { get; private set; }

    public decimal CapPercent { get; private set; }

    public int EmployeeCount { get; private set; }

    public decimal TotalApplied { get; private set; }

    public decimal TotalCarriedForward { get; private set; }

    public decimal TotalRefunds { get; private set; }

    public decimal TotalSettledManually { get; private set; }

    public int GeneratedByEmployeeId { get; private set; }

    public DateTime GeneratedAtUtc { get; private set; }

    public int? ReviewedByEmployeeId { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public int? ApprovedByEmployeeId { get; private set; }

    public DateTime? ApprovedAtUtc { get; private set; }

    public int? FinalizedByEmployeeId { get; private set; }

    public DateTime? FinalizedAtUtc { get; private set; }

    public int? LockedByEmployeeId { get; private set; }

    public DateTime? LockedAtUtc { get; private set; }

    public DateTime? SupersededAtUtc { get; private set; }

    /// <summary>Result of the checks run at finalization, as JSON.</summary>
    public string? ReconciliationJson { get; private set; }

    public int Version { get; set; }

    public IReadOnlyCollection<PayrollRunEmployee> Employees => _employees;

    public bool IsOpen => Status is PayrollRunStatus.Preview or PayrollRunStatus.Reviewed or PayrollRunStatus.Approved;

    public void MarkReviewed(int financeId, DateTime utcNow)
    {
        Ensure(PayrollRunStatus.Preview, PayrollRunStatus.Reviewed);
        ReviewedByEmployeeId = financeId;
        ReviewedAtUtc = utcNow;
        Status = PayrollRunStatus.Reviewed;
    }

    /// <summary>The second approver. Must differ from whoever generated and reviewed the run.</summary>
    public void Approve(int managerId, string currentInputsHash, DateTime utcNow)
    {
        Ensure(PayrollRunStatus.Reviewed, PayrollRunStatus.Approved);
        if (managerId == GeneratedByEmployeeId || managerId == ReviewedByEmployeeId)
        {
            throw new DomainForbiddenException("payroll.same_approver", "The run must be approved by someone other than the person who generated or reviewed it.");
        }

        EnsureInputsUnchanged(InputsHash, currentInputsHash);
        ApprovedInputsHash = InputsHash;
        ApprovedByEmployeeId = managerId;
        ApprovedAtUtc = utcNow;
        Status = PayrollRunStatus.Approved;
    }

    public void Finalize(int financeId, string currentInputsHash, string reconciliationJson, DateTime utcNow)
    {
        Ensure(PayrollRunStatus.Approved, PayrollRunStatus.Finalized);
        EnsureInputsUnchanged(ApprovedInputsHash!, currentInputsHash);
        FinalizedByEmployeeId = financeId;
        FinalizedAtUtc = utcNow;
        ReconciliationJson = reconciliationJson;
        Status = PayrollRunStatus.Finalized;
    }

    public void Lock(int financeId, DateTime utcNow)
    {
        Ensure(PayrollRunStatus.Finalized, PayrollRunStatus.Locked);
        LockedByEmployeeId = financeId;
        LockedAtUtc = utcNow;
        Status = PayrollRunStatus.Locked;
    }

    public void Supersede(DateTime utcNow)
    {
        if (!IsOpen)
        {
            throw new DomainException("payroll.invalid_transition", $"A {Status} run cannot be superseded.");
        }

        SupersededAtUtc = utcNow;
        Status = PayrollRunStatus.Superseded;
    }

    private void Ensure(PayrollRunStatus from, PayrollRunStatus to)
    {
        if (Status != from)
        {
            throw new DomainException("payroll.invalid_transition", $"A {Status} run cannot move to {to}; it must be {from}.");
        }
    }

    private static void EnsureInputsUnchanged(string expected, string current)
    {
        if (!string.Equals(expected, current, StringComparison.Ordinal))
        {
            throw new DomainException("payroll.inputs_changed", "Deduction lines, pay or policy changed since this run was generated. Generate a new version.");
        }
    }
}
