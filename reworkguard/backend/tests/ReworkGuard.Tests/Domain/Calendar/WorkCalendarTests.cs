using ReworkGuard.Domain.Calendar;
using ReworkGuard.Domain.Common;

namespace ReworkGuard.Tests.Domain.Calendar;

public sealed class WorkCalendarTests
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    // ---------- IST work dates ----------

    [Theory]
    [InlineData(2026, 3, 9, 18, 29, "2026-03-09")] // 23:59 IST
    [InlineData(2026, 3, 9, 18, 30, "2026-03-10")] // 00:00 IST next day
    [InlineData(2026, 1, 31, 18, 29, "2026-01-31")] // last minute of January in IST
    [InlineData(2026, 1, 31, 18, 30, "2026-02-01")] // first minute of February in IST, still 31 Jan in UTC
    [InlineData(2028, 2, 28, 18, 30, "2028-02-29")] // leap day starts in IST
    [InlineData(2028, 2, 29, 18, 30, "2028-03-01")]
    [InlineData(2026, 12, 31, 18, 30, "2027-01-01")] // year boundary
    public void Utc_instants_map_to_the_ist_calendar_date(int y, int m, int d, int h, int min, string expected)
    {
        WorkTimeZone.ToWorkDate(Utc(y, m, d, h, min)).Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void An_ist_month_boundary_puts_late_utc_evening_work_in_the_next_month()
    {
        var workDate = WorkTimeZone.ToWorkDate(Utc(2026, 1, 31, 20, 0));

        YearMonth.From(workDate).Should().Be(YearMonth.Parse("2026-02"));
        YearMonth.From(DateOnly.FromDateTime(Utc(2026, 1, 31, 20, 0))).Should().Be(YearMonth.Parse("2026-01"), "the naive UTC date would say January");
    }

    [Fact]
    public void Ist_day_boundaries_are_five_and_a_half_hours_before_utc_midnight()
    {
        var day = new DateOnly(2026, 3, 10);

        WorkTimeZone.StartOfDayUtc(day).Should().Be(Utc(2026, 3, 9, 18, 30));
        WorkTimeZone.EndOfDayUtc(day).Should().Be(Utc(2026, 3, 10, 18, 30).AddTicks(-10));
        WorkTimeZone.ToWorkDate(WorkTimeZone.EndOfDayUtc(day)).Should().Be(day);
        WorkTimeZone.ToWorkDate(WorkTimeZone.EndOfDayUtc(day).AddTicks(10)).Should().Be(day.AddDays(1));
    }

    [Fact]
    public void Leap_day_has_a_full_ist_day()
    {
        var leap = new DateOnly(2028, 2, 29);

        (WorkTimeZone.EndOfDayUtc(leap) - WorkTimeZone.StartOfDayUtc(leap)).Should().Be(TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(10)));
    }

    // ---------- Working days ----------

    [Theory]
    [InlineData("2026-03-09", 3, "2026-03-12")] // Monday -> Thursday
    [InlineData("2026-03-13", 3, "2026-03-18")] // Friday -> Wednesday (weekend skipped)
    [InlineData("2026-03-14", 3, "2026-03-18")] // Saturday -> Wednesday
    [InlineData("2026-03-15", 1, "2026-03-16")] // Sunday -> Monday
    [InlineData("2026-03-12", 1, "2026-03-13")] // Thursday -> Friday
    [InlineData("2026-03-13", 1, "2026-03-16")] // Friday -> Monday
    [InlineData("2028-02-25", 3, "2028-03-01")] // Friday before leap day: Mon 28, Tue 29, Wed 1
    public void Weekends_are_skipped(string start, int days, string expected)
    {
        WorkingDays.AddWorkingDays(DateOnly.Parse(start), days, NoHolidays).Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Holidays_are_skipped_like_weekends()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 3, 16), new(2026, 3, 17) };

        WorkingDays.AddWorkingDays(new DateOnly(2026, 3, 13), 3, holidays).Should().Be(new DateOnly(2026, 3, 20));
        WorkingDays.IsWorkingDay(new DateOnly(2026, 3, 16), holidays).Should().BeFalse();
        WorkingDays.IsWorkingDay(new DateOnly(2026, 3, 18), holidays).Should().BeTrue();
    }

    [Fact]
    public void A_holiday_on_a_weekend_changes_nothing()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 3, 14) };

        WorkingDays.AddWorkingDays(new DateOnly(2026, 3, 13), 3, holidays).Should().Be(new DateOnly(2026, 3, 18));
    }

    [Fact]
    public void At_least_one_working_day_is_required()
    {
        var act = () => WorkingDays.AddWorkingDays(new DateOnly(2026, 3, 13), 0, NoHolidays);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("calendar.invalid_count");
    }

    [Fact]
    public void A_window_opened_friday_evening_ist_ends_wednesday_midnight_ist()
    {
        // Friday 13 March 2026, 20:00 IST = 14:30 UTC.
        var end = WorkingDays.WindowEndUtc(Utc(2026, 3, 13, 14, 30), 3, NoHolidays);

        end.Should().Be(Utc(2026, 3, 18, 18, 30).AddTicks(-10));
    }

    [Fact]
    public void A_window_opened_just_after_ist_midnight_counts_from_that_ist_day()
    {
        // 18:45 UTC on Thursday 12 March is 00:15 IST on Friday 13 March.
        var end = WorkingDays.WindowEndUtc(Utc(2026, 3, 12, 18, 45), 3, NoHolidays);

        WorkTimeZone.ToWorkDate(end).Should().Be(new DateOnly(2026, 3, 18));
    }

    [Fact]
    public void Time_zone_resolves_to_india_standard_time()
    {
        WorkTimeZone.Zone.GetUtcOffset(Utc(2026, 7, 1, 0)).Should().Be(TimeSpan.FromHours(5.5));
        WorkTimeZone.Zone.GetUtcOffset(Utc(2026, 1, 1, 0)).Should().Be(TimeSpan.FromHours(5.5), "IST has no daylight saving");
    }
}
