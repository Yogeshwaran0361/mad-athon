using ReworkGuard.Domain.Common;

namespace ReworkGuard.Tests.Domain;

public sealed class YearMonthTests
{
    [Theory]
    [InlineData("2026-01", "2026-02")]
    [InlineData("2026-11", "2026-12")]
    [InlineData("2026-12", "2027-01")]
    [InlineData("1999-12", "2000-01")]
    public void Next_rolls_December_into_January(string month, string next)
    {
        YearMonth.Parse(month).Next().ToString().Should().Be(next);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-1")]
    [InlineData("26-01")]
    [InlineData("2026/01")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_months_do_not_parse(string? value)
    {
        YearMonth.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Contains_and_bounds_cover_the_whole_month()
    {
        var feb = YearMonth.Parse("2028-02");

        feb.FirstDay.Should().Be(new DateOnly(2028, 2, 1));
        feb.LastDay.Should().Be(new DateOnly(2028, 2, 29));
        feb.Contains(new DateOnly(2028, 2, 29)).Should().BeTrue();
        feb.Contains(new DateOnly(2028, 3, 1)).Should().BeFalse();
    }

    [Fact]
    public void Months_order_chronologically_across_years()
    {
        (YearMonth.Parse("2026-12") < YearMonth.Parse("2027-01")).Should().BeTrue();
        (YearMonth.Parse("2027-01") > YearMonth.Parse("2026-12")).Should().BeTrue();
    }
}
