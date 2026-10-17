using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Infrastructure.Services;

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
