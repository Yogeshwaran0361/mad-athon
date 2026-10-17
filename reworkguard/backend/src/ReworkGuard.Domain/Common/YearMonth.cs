using System.Globalization;

namespace ReworkGuard.Domain.Common;

/// <summary>A calendar month, formatted as yyyy-MM.</summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    public YearMonth(int year, int month)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);

    public static bool TryParse(string? value, out YearMonth result)
    {
        result = default;
        if (value is null
            || !DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        result = new YearMonth(parsed.Year, parsed.Month);
        return true;
    }

    public static YearMonth Parse(string value) =>
        TryParse(value, out var result) ? result : throw new FormatException($"'{value}' is not a valid yyyy-MM month.");

    public YearMonth Next() => Month == 12 ? new YearMonth(Year + 1, 1) : new YearMonth(Year, Month + 1);

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    public int CompareTo(YearMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;

    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;

    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;

    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}
