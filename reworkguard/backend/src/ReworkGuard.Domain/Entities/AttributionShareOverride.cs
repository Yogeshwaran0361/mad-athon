namespace ReworkGuard.Domain.Entities;

/// <summary>A manager-set share of a bug's deduction for one original deliverer, replacing the computed split.</summary>
public class AttributionShareOverride
{
    private AttributionShareOverride()
    {
    }

    public AttributionShareOverride(int bugId, int employeeId, decimal sharePercent, int setByEmployeeId, DateTime setAtUtc, string justification)
    {
        BugId = bugId;
        EmployeeId = employeeId;
        SharePercent = sharePercent;
        SetByEmployeeId = setByEmployeeId;
        SetAtUtc = setAtUtc;
        Justification = justification;
    }

    public int Id { get; private set; }

    public int BugId { get; private set; }

    public int EmployeeId { get; private set; }

    public decimal SharePercent { get; private set; }

    public int SetByEmployeeId { get; private set; }

    public DateTime SetAtUtc { get; private set; }

    public string Justification { get; private set; } = string.Empty;
}
