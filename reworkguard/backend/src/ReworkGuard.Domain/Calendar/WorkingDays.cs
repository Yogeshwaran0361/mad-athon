using ReworkGuard.Domain.Common;

namespace ReworkGuard.Domain.Calendar;

/// <summary>Working-day arithmetic: Saturdays, Sundays and configured holidays are not working days.</summary>
public static class WorkingDays
{
    public static bool IsWorkingDay(DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date);

    /// <summary>
    /// The <paramref name="count"/>-th working day after <paramref name="start"/>; <paramref name="start"/> itself never counts.
    /// Notified on a Friday with a 3-day window gives the following Wednesday.
    /// </summary>
    public static DateOnly AddWorkingDays(DateOnly start, int count, IReadOnlySet<DateOnly> holidays)
    {
        if (count < 1)
        {
            throw new DomainException("calendar.invalid_count", "The number of working days must be at least 1.");
        }

        var date = start;
        var found = 0;
        while (found < count)
        {
            date = date.AddDays(1);
            if (IsWorkingDay(date, holidays))
            {
                found++;
            }
        }

        return date;
    }

    /// <summary>
    /// End of a dispute window opened at <paramref name="openedAtUtc"/>: the end (23:59:59.999999 IST) of the
    /// <paramref name="workingDays"/>-th working day after the IST date the window opened, as a UTC instant.
    /// </summary>
    public static DateTime WindowEndUtc(DateTime openedAtUtc, int workingDays, IReadOnlySet<DateOnly> holidays) =>
        WorkTimeZone.EndOfDayUtc(AddWorkingDays(WorkTimeZone.ToWorkDate(openedAtUtc), workingDays, holidays));
}
