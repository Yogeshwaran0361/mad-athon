using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Payroll;

namespace ReworkGuard.Application.Payroll;

/// <summary>
/// Pay in force on a date, from <see cref="EmployeeRateHistory"/>. Loads each employee's history once per instance,
/// so a scoped instance can value many lines cheaply.
/// </summary>
public sealed class HourlyRateResolver
{
    private readonly IApplicationDbContext _db;
    private readonly Dictionary<int, List<EmployeeRateHistory>> _cache = [];

    public HourlyRateResolver(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> HourlyRateAsync(int employeeId, DateOnly workDate, DeductionPolicy policy, CancellationToken cancellationToken) =>
        HourlyRateCalculator.HourlyRate(await PayOnAsync(employeeId, workDate, cancellationToken), policy);

    public async Task<PayProfile> PayOnAsync(int employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(employeeId, out var history))
        {
            history = await _db.EmployeeRateHistory.AsNoTracking().Where(r => r.EmployeeId == employeeId).ToListAsync(cancellationToken);
            _cache[employeeId] = history;
        }

        return RateHistory.EffectiveOn(history, date).PayProfile;
    }

    /// <summary>Loads the history of many employees in one query.</summary>
    public async Task PreloadAsync(IReadOnlyCollection<int> employeeIds, CancellationToken cancellationToken)
    {
        var missing = employeeIds.Where(id => !_cache.ContainsKey(id)).Distinct().ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var rows = await _db.EmployeeRateHistory.AsNoTracking().Where(r => missing.Contains(r.EmployeeId)).ToListAsync(cancellationToken);
        foreach (var id in missing)
        {
            _cache[id] = rows.Where(r => r.EmployeeId == id).ToList();
        }
    }
}
