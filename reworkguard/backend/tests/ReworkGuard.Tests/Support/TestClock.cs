using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Tests.Support;

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
