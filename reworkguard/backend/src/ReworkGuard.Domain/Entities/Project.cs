using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Entities;

public class Project
{
    public const int DefaultWarrantyDays = 45;

    private Project()
    {
    }

    public Project(string name, int warrantyDays = DefaultWarrantyDays)
    {
        if (warrantyDays < 0)
        {
            throw new DomainException("project.invalid_warranty", "Warranty days cannot be negative.");
        }

        Name = name;
        WarrantyDays = warrantyDays;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int WarrantyDays { get; private set; }

    /// <summary>Changes the window for bugs reported from now on; existing bugs keep the verdict computed when reported.</summary>
    public void ChangeWarranty(int warrantyDays)
    {
        if (warrantyDays is < 0 or > 3650)
        {
            throw new DomainException("project.invalid_warranty", "Warranty days must be between 0 and 3650.");
        }

        WarrantyDays = warrantyDays;
    }
}
