namespace ReworkGuard.Application.Abstractions;

/// <summary>Work dates in Asia/Kolkata and working-day windows that skip weekends and company holidays.</summary>
public interface IWorkCalendar
{
    /// <summary>Today's date in IST.</summary>
    DateOnly Today { get; }

    /// <summary>End (UTC) of a window of <paramref name="workingDays"/> working days opened at <paramref name="openedAtUtc"/>.</summary>
    Task<DateTime> WindowEndUtcAsync(DateTime openedAtUtc, int workingDays, CancellationToken cancellationToken);
}
