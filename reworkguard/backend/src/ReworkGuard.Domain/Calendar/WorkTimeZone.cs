namespace ReworkGuard.Domain.Calendar;

/// <summary>
/// Work dates (the day hours were worked, month boundaries, dispute windows) are calendar dates in Asia/Kolkata.
/// Timestamps are stored in UTC and converted here.
/// </summary>
public static class WorkTimeZone
{
    public const string Id = "Asia/Kolkata";

    /// <summary>India Standard Time. Falls back to a fixed +05:30 zone (IST has no daylight saving) when tzdata is missing.</summary>
    public static readonly TimeZoneInfo Zone = Resolve();

    public static DateOnly ToWorkDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utc), Zone));

    /// <summary>The UTC instant at which <paramref name="date"/> starts in IST.</summary>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);

    /// <summary>
    /// The last microsecond of <paramref name="date"/> in IST, as UTC (inclusive end of day). Microseconds, not ticks,
    /// because that is what MySQL datetime(6) stores, so the value round-trips exactly.
    /// </summary>
    public static DateTime EndOfDayUtc(DateOnly date) => StartOfDayUtc(date.AddDays(1)).AddTicks(-OneMicrosecond);

    private const long OneMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(Id, TimeSpan.FromHours(5.5), "India Standard Time", "IST");
        }
    }
}
