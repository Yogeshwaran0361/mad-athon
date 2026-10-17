using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Domain.Entities;

/// <summary>
/// An employee's pay, effective from a date until the next entry. Corrective work is valued at the rate valid on
/// its work date, and the deduction cap at the pay valid in the payroll month.
/// </summary>
public class EmployeeRateHistory
{
    private EmployeeRateHistory()
    {
    }

    internal EmployeeRateHistory(DateOnly effectiveFrom, PayProfile pay, int? recordedByEmployeeId, DateTime? recordedAtUtc)
    {
        EffectiveFrom = effectiveFrom;
        PayMode = pay.PayMode;
        MonthlyGross = pay.MonthlyGross;
        DailyRate = pay.DailyRate;
        HourlyRate = pay.HourlyRate;
        RecordedByEmployeeId = recordedByEmployeeId;
        RecordedAtUtc = recordedAtUtc;
    }

    public int Id { get; private set; }

    public int EmployeeId { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public PayMode PayMode { get; private set; }

    public decimal? MonthlyGross { get; private set; }

    public decimal? DailyRate { get; private set; }

    public decimal? HourlyRate { get; private set; }

    public int? RecordedByEmployeeId { get; private set; }

    /// <summary>When an admin recorded the entry; null for the pay given when the employee was created.</summary>
    public DateTime? RecordedAtUtc { get; private set; }

    public PayProfile PayProfile => new(PayMode, MonthlyGross, DailyRate, HourlyRate);
}
