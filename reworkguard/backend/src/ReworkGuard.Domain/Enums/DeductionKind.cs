namespace ReworkGuard.Domain.Enums;

public enum DeductionKind
{
    /// <summary>Generated from approved corrective hours on a deductible bug. Amount is zero or positive.</summary>
    Standard,

    /// <summary>Reverses a finalized line in a later run. Amount is negative and the line names the one it reverses.</summary>
    Compensating,
}
