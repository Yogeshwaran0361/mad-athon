using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace ReworkGuard.Infrastructure.Jobs;

/// <summary>
/// A short-lived, encrypted cookie that lets an administrator open the Hangfire dashboard in a browser tab
/// (the API itself uses bearer tokens, which a plain page navigation cannot send).
/// </summary>
public sealed class DashboardSession
{
    public const string CookieName = "rg_jobs";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector;

    public DashboardSession(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ReworkGuard.JobsDashboard.v1").ToTimeLimitedDataProtector();
    }

    public string Issue(int employeeId) => _protector.Protect(employeeId.ToString(CultureInfo.InvariantCulture), Lifetime);

    public bool TryRead(string? value, out int employeeId)
    {
        employeeId = 0;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            return int.TryParse(_protector.Unprotect(value), NumberStyles.None, CultureInfo.InvariantCulture, out employeeId);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}
