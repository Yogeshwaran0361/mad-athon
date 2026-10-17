using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Payroll;
using ReworkGuard.Domain.Policies;

namespace ReworkGuard.Application.Payroll;

/// <summary>Reads the policy from the PolicySettings table.</summary>
public sealed class PolicyProvider
{
    private readonly IApplicationDbContext _db;

    public PolicyProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DeductionPolicy> GetDeductionPolicyAsync(CancellationToken cancellationToken) =>
        (await GetSnapshotAsync(cancellationToken)).Deduction;

    public async Task<PolicySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.PolicySettings.AsNoTracking().ToDictionaryAsync(p => p.Key, p => p.Value, cancellationToken);
        return FromSettings(settings);
    }

    public static PolicySnapshot FromSettings(IReadOnlyDictionary<string, string> settings) => new(
        ReadDecimal(settings, PolicySetting.StandardHoursPerMonth),
        ReadDecimal(settings, PolicySetting.DailyHours),
        ReadDecimal(settings, PolicySetting.DeductionCapPercent),
        (int)ReadDecimal(settings, PolicySetting.DefaultWarrantyDays),
        (int)ReadDecimal(settings, PolicySetting.DisputeWindowDays),
        ReadBool(settings, PolicySetting.QaMissDeductible),
        ReadDecimal(settings, PolicySetting.QaMissSharePercent),
        ReadBool(settings, PolicySetting.RequireEmployeeAcknowledgement));

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Policy setting '{key}' is missing or not a number.");

    private static bool ReadBool(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value)
            ? value
            : throw new InvalidOperationException($"Policy setting '{key}' is missing or not true/false.");
}
