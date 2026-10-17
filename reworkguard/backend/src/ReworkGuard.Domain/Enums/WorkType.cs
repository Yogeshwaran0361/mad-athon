namespace ReworkGuard.Domain.Enums;

public enum WorkType
{
    Regular,
    Corrective,

    /// <summary>Paid new work on a change request task. Never part of the rework or deduction flow.</summary>
    Enhancement,
}
