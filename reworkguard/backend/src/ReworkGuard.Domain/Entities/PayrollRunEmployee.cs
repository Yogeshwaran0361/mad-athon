using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>One employee's result in a run, with the name and pay basis as they were when the run was generated. Append-only.</summary>
public class PayrollRunEmployee : IAppendOnly
{
    private readonly List<PayrollRunItem> _items = [];

    private PayrollRunEmployee()
    {
    }

    internal PayrollRunEmployee(
        int employeeId,
        string employeeCode,
        string employeeName,
        PayMode payMode,
        string salaryBasis,
        Payroll.PayrollEmployeeResult result)
    {
        EmployeeId = employeeId;
        EmployeeCode = employeeCode;
        EmployeeName = employeeName;
        PayMode = payMode;
        SalaryBasis = salaryBasis.Length > 300 ? salaryBasis[..300] : salaryBasis;
        ManualSettlement = result.ManualSettlement;
        SalaryBase = result.SalaryBase;
        CapAmount = result.CapAmount;
        CarriedIn = result.CarriedIn;
        CurrentDue = result.CurrentDue;
        Refunds = result.Refunds;
        CancelledByCompensation = result.CancelledByCompensation;
        Applied = result.Applied;
        CarriedForward = result.CarriedForward;
        SettledManually = result.SettledManually;
        NetAdjustment = result.NetAdjustment;
        Earnings = result.Earnings;
        foreach (var item in result.Items)
        {
            _items.Add(new PayrollRunItem(item));
        }
    }

    public long Id { get; private set; }

    public int PayrollRunId { get; private set; }

    public int EmployeeId { get; private set; }

    public string EmployeeCode { get; private set; } = string.Empty;

    public string EmployeeName { get; private set; } = string.Empty;

    public PayMode PayMode { get; private set; }

    /// <summary>How the salary base was worked out, e.g. "Monthly gross in force on 2026-04-01".</summary>
    public string SalaryBasis { get; private set; } = string.Empty;

    public bool ManualSettlement { get; private set; }

    public decimal SalaryBase { get; private set; }

    public decimal CapAmount { get; private set; }

    public decimal CarriedIn { get; private set; }

    public decimal CurrentDue { get; private set; }

    public decimal Refunds { get; private set; }

    public decimal CancelledByCompensation { get; private set; }

    public decimal Applied { get; private set; }

    public decimal CarriedForward { get; private set; }

    public decimal SettledManually { get; private set; }

    public decimal NetAdjustment { get; private set; }

    /// <summary>Profit-share earnings paid in this run, shown as a separate line.</summary>
    public decimal Earnings { get; private set; }

    public IReadOnlyCollection<PayrollRunItem> Items => _items;
}
