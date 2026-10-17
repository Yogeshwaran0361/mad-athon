using System.ComponentModel.DataAnnotations;

namespace ReworkGuard.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Consecutive failed sign-ins that lock an account.</summary>
    [Range(3, 20)]
    public int MaxFailedLogins { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Send the refresh cookie only over HTTPS. Turned off only for local HTTP development and tests.</summary>
    public bool SecureCookie { get; set; } = true;
}
