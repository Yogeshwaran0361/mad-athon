using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Domain.Payroll;

/// <summary>
/// The deduction engine only ever values corrective hours. Enhancement hours are paid change-request work; if one
/// reaches the engine something upstream is broken, so it fails loudly instead of deducting.
/// </summary>
public static class DeductionWorkTypeGuard
{
    public static void EnsureDeductible(int timeLogId, WorkType workType)
    {
        if (workType == WorkType.Enhancement)
        {
            throw new InvalidOperationException(
                $"Time log {timeLogId} is Enhancement work on a change request and must never reach the deduction engine.");
        }

        if (workType != WorkType.Corrective)
        {
            throw new InvalidOperationException($"Time log {timeLogId} is {workType} work; only corrective hours can be deducted.");
        }
    }
}
