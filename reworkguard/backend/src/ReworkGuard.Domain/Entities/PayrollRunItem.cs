using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Entities;

/// <summary>How one deduction line was treated in a run. Append-only; the next run's carry-forward is read from here.</summary>
public class PayrollRunItem : IAppendOnly
{
    private PayrollRunItem()
    {
    }

    internal PayrollRunItem(Payroll.PayrollItemResult item)
    {
        DeductionId = item.DeductionId;
        Kind = item.Kind;
        OriginalApplyMonth = item.OriginalApplyMonth.ToString();
        WorkDate = item.WorkDate;
        Due = item.Due;
        Applied = item.Applied;
        CarriedForward = item.CarriedForward;
        SettledManually = item.SettledManually;
        CancelledByCompensation = item.CancelledByCompensation;
    }

    public long Id { get; private set; }

    public long PayrollRunEmployeeId { get; private set; }

    public int DeductionId { get; private set; }

    public PayrollItemKind Kind { get; private set; }

    public string OriginalApplyMonth { get; private set; } = string.Empty;

    public DateOnly WorkDate { get; private set; }

    public decimal Due { get; private set; }

    public decimal Applied { get; private set; }

    public decimal CarriedForward { get; private set; }

    public decimal SettledManually { get; private set; }

    public decimal CancelledByCompensation { get; private set; }
}
