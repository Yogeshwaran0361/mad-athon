namespace ReworkGuard.Domain.Enums;

public enum PayrollItemKind
{
    /// <summary>Outstanding part of a line that did not fit under an earlier month's cap.</summary>
    CarriedIn,

    /// <summary>An approved deduction line due in this run.</summary>
    Current,

    /// <summary>A compensating line: money returned to the employee. Never capped.</summary>
    Refund,
}
