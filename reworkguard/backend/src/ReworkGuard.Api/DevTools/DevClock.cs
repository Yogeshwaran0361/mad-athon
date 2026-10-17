using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Api.DevTools;

/// <summary>
/// System time plus an offset that only moves forward. Registered in place of the system clock only when
/// <see cref="DevToolsExtensions.IsClockControlEnabled"/> holds (Development and DevTools:ClockControl=true),
/// so local demos can step past the dispute window without waiting days.
/// </summary>
public sealed class DevClock : IClock
{
    private long _offsetTicks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public DateTime UtcNow => DateTime.UtcNow + Offset;

    public void Advance(TimeSpan by)
    {
        if (by <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "The clock can only move forward.");
        }

        Interlocked.Add(ref _offsetTicks, by.Ticks);
    }
}
