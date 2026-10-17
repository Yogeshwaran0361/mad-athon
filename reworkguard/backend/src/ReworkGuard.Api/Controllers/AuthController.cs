using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Application.Auth;
using ReworkGuard.Application.Employees;

namespace ReworkGuard.Api.Controllers;

/// <summary>
/// Sign-in, token refresh and sign-out. The access token is returned in the body (kept in memory by the client); the
/// refresh token travels only in an HttpOnly, SameSite=Strict cookie scoped to this controller's path.
/// </summary>
[ApiController]
[Route($"{ApiRoutes.V1}/auth")]
public sealed class AuthController : ControllerBase
{
    public const string RefreshCookie = "rg_refresh";
    private const string CookiePath = "/" + ApiRoutes.V1 + "/auth";

    private readonly AuthService _auth;
    private readonly AuthOptions _options;

    public AuthController(AuthService auth, IOptions<AuthOptions> options)
    {
        _auth = auth;
        _options = options.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        WithCookie(await _auth.LoginAsync(request, cancellationToken));

    /// <summary>Rotates the refresh cookie and returns a new access token. A reused refresh token ends all its sessions.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<LoginResponse> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            return WithCookie(await _auth.RefreshAsync(Request.Cookies[RefreshCookie], cancellationToken));
        }
        catch (Application.Common.AuthenticationFailedException)
        {
            ClearCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(Request.Cookies[RefreshCookie], cancellationToken);
        ClearCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Roles = Roles.Everyone)]
    public Task<EmployeeDto> Me(CancellationToken cancellationToken) => _auth.CurrentAsync(cancellationToken);

    private LoginResponse WithCookie(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, CookieOptions(TimeSpan.FromDays(_options.RefreshTokenDays)));
        Response.Headers.CacheControl = "no-store";
        return result.Response;
    }

    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, CookieOptions(null));

    // Max-Age, not Expires: the browser measures it from receipt, so client and server clocks need not agree.
    private CookieOptions CookieOptions(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = _options.SecureCookie,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        MaxAge = maxAge,
        IsEssential = true,
    };
}
