using ReworkGuard.Domain.Attribution;
using ReworkGuard.Domain.Common;

namespace ReworkGuard.Tests.Domain;

public sealed class AttributionCalculatorTests
{
    [Fact]
    public void Shares_follow_approved_regular_hours()
    {
        var shares = AttributionCalculator.ComputeShares([new DelivererHours(1, 6m), new DelivererHours(2, 2m)]);

        shares.Should().Equal(new AttributionShare(1, 75.00m), new AttributionShare(2, 25.00m));
    }

    [Fact]
    public void Without_regular_hours_the_split_is_equal()
    {
        var shares = AttributionCalculator.ComputeShares([new DelivererHours(1, 0m), new DelivererHours(2, 0m)]);

        shares.Should().Equal(new AttributionShare(1, 50.00m), new AttributionShare(2, 50.00m));
    }

    [Fact]
    public void A_three_way_equal_split_sums_to_exactly_100_with_the_remainder_on_one_share()
    {
        var shares = AttributionCalculator.ComputeShares([new DelivererHours(3, 0m), new DelivererHours(1, 0m), new DelivererHours(2, 0m)]);

        shares.Sum(s => s.SharePercent).Should().Be(100.00m);
        shares.Should().Equal(new AttributionShare(1, 33.34m), new AttributionShare(2, 33.33m), new AttributionShare(3, 33.33m));
    }

    [Fact]
    public void A_three_way_hours_split_puts_the_rounding_remainder_on_the_largest_share()
    {
        // Raw: 14.2857 / 28.5714 / 57.1428 -> rounded 14.29 + 28.57 + 57.14 = 100.00 already; use 1:1:1 weighted by thirds of 10h.
        var shares = AttributionCalculator.ComputeShares([new DelivererHours(1, 1m), new DelivererHours(2, 1m), new DelivererHours(3, 4m)]);

        // Raw: 16.6667 / 16.6667 / 66.6667 -> 16.67 + 16.67 + 66.67 = 100.01, so the largest share absorbs -0.01.
        shares.Should().Equal(new AttributionShare(1, 16.67m), new AttributionShare(2, 16.67m), new AttributionShare(3, 66.66m));
        shares.Sum(s => s.SharePercent).Should().Be(100.00m);
    }

    [Fact]
    public void A_sole_deliverer_carries_the_full_share()
    {
        AttributionCalculator.ComputeShares([new DelivererHours(7, 12.5m)]).Should().Equal(new AttributionShare(7, 100.00m));
    }

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        var none = () => AttributionCalculator.ComputeShares([]);
        var duplicate = () => AttributionCalculator.ComputeShares([new DelivererHours(1, 1m), new DelivererHours(1, 2m)]);
        var negative = () => AttributionCalculator.ComputeShares([new DelivererHours(1, -1m)]);

        none.Should().Throw<DomainException>().Which.Code.Should().Be("attribution.no_deliverers");
        duplicate.Should().Throw<DomainException>().Which.Code.Should().Be("attribution.duplicate_deliverer");
        negative.Should().Throw<DomainException>().Which.Code.Should().Be("attribution.negative_hours");
    }

    [Fact]
    public void Property_shares_always_sum_to_exactly_100_and_stay_close_to_the_raw_proportion()
    {
        var random = new Random(20260316);
        for (var run = 0; run < 2000; run++)
        {
            var count = random.Next(1, 9);
            var deliverers = Enumerable.Range(1, count)
                .Select(id => new DelivererHours(id, random.Next(0, 4) == 0 ? 0m : Math.Round((decimal)random.NextDouble() * 80m, 2)))
                .ToList();
            var total = deliverers.Sum(d => d.ApprovedRegularHours);

            var shares = AttributionCalculator.ComputeShares(deliverers);

            shares.Sum(s => s.SharePercent).Should().Be(100.00m, "run {0}", run);
            shares.Should().OnlyContain(s => s.SharePercent >= 0 && decimal.Round(s.SharePercent, 2) == s.SharePercent);
            foreach (var share in shares)
            {
                var hours = deliverers.Single(d => d.EmployeeId == share.EmployeeId).ApprovedRegularHours;
                var raw = total == 0 ? 100m / count : hours / total * 100m;
                Math.Abs(share.SharePercent - raw).Should().BeLessThanOrEqualTo(0.005m * count + 0.005m);
            }
        }
    }

    [Fact]
    public void A_valid_override_passes()
    {
        var act = () => AttributionCalculator.ValidateOverride([new AttributionShare(1, 60.5m), new AttributionShare(2, 39.5m)], [1, 2]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(new[] { 1, 2 }, new[] { 60.0, 39.99 }, "attribution.not_100")]
    [InlineData(new[] { 1, 3 }, new[] { 50.0, 50.0 }, "attribution.not_original_deliverer")]
    [InlineData(new[] { 1, 1 }, new[] { 50.0, 50.0 }, "attribution.duplicate_deliverer")]
    [InlineData(new[] { 1, 2 }, new[] { 100.0, 0.0 }, "attribution.invalid_share")]
    [InlineData(new[] { 1, 2 }, new[] { 33.333, 66.667 }, "attribution.invalid_share")]
    [InlineData(new int[0], new double[0], "attribution.empty")]
    public void Invalid_overrides_are_rejected(int[] employeeIds, double[] percents, string code)
    {
        var shares = employeeIds.Zip(percents, (id, p) => new AttributionShare(id, (decimal)p)).ToList();

        var act = () => AttributionCalculator.ValidateOverride(shares, [1, 2]);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }
}
