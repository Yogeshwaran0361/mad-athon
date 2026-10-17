namespace ReworkGuard.Application.Deductions;

/// <summary>Deduction settings from configuration (section "Deductions").</summary>
public sealed class DeductionOptions
{
    public const string SectionName = "Deductions";

    /// <summary>
    /// When true (the default), a notified line can be approved only after its dispute window closes. Setting it to
    /// false lets a manager approve straight away; employees can still dispute until the line is approved.
    /// </summary>
    public bool EnforceDisputeWindow { get; set; } = true;
}
