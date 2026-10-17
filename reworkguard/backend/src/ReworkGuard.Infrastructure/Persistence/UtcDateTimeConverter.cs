using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ReworkGuard.Infrastructure.Persistence;

/// <summary>Stores DateTime as UTC and marks values read back from MySQL as UTC.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
