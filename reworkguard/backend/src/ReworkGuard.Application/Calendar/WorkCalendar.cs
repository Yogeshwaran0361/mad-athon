using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Calendar;

namespace ReworkGuard.Application.Calendar;

public sealed class WorkCalendar : IWorkCalendar
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public WorkCalendar(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateOnly Today => WorkTimeZone.ToWorkDate(_clock.UtcNow);

    public async Task<DateTime> WindowEndUtcAsync(DateTime openedAtUtc, int workingDays, CancellationToken cancellationToken)
    {
        var from = WorkTimeZone.ToWorkDate(openedAtUtc);
        // A window of n working days never spans more than n weeks plus the holidays inside it; a year is ample.
        var to = from.AddDays(366);
        var holidays = await _db.Holidays.AsNoTracking()
            .Where(h => h.Date > from && h.Date <= to)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);
        return WorkingDays.WindowEndUtc(openedAtUtc, workingDays, holidays.ToHashSet());
    }
}
