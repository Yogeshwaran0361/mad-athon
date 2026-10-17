using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Entities;

/// <summary>A non-working day in the company calendar. Dispute windows skip holidays as well as weekends.</summary>
public class Holiday
{
    private Holiday()
    {
    }

    public Holiday(DateOnly date, string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 100)
        {
            throw new DomainException("holiday.invalid_name", "A holiday needs a name of 1 to 100 characters.");
        }

        Date = date;
        Name = trimmed;
    }

    public int Id { get; private set; }

    public DateOnly Date { get; private set; }

    public string Name { get; private set; } = string.Empty;
}
