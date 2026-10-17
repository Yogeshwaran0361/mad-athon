using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Employees;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Auth;

/// <summary>A successful sign-in or refresh: the response body plus the refresh token for the HttpOnly cookie.</summary>
public sealed record AuthResult(LoginResponse Response, string RefreshToken, DateTime RefreshExpiresAtUtc);

/// <summary>
/// Sign-in with lockout after repeated failures, short-lived access tokens and rotating refresh tokens with reuse
/// detection. Every outcome that matters for security is audited; failures never reveal whether the account exists.
/// </summary>
public sealed class AuthService
{
    // Verified against when the email is unknown, so unknown and known accounts take the same time to reject.
    private static string? s_dummyHash;

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly IValidator<LoginRequest> _validator;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly AuthOptions _options;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        ICurrentUser currentUser,
        IValidator<LoginRequest> validator,
        IClock clock,
        IAuditWriter audit,
        IOptions<AuthOptions> options)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _currentUser = currentUser;
        _validator = validator;
        _clock = clock;
        _audit = audit;
        _options = options.Value;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var email = request.Email.Trim();
        var id = await _db.Employees.AsNoTracking().Where(e => e.Email == email).Select(e => (int?)e.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is null)
        {
            _hasher.Verify(s_dummyHash ??= _hasher.Hash(Guid.NewGuid().ToString("N")), request.Password);
            throw new AuthenticationFailedException();
        }

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _db.LockEmployeeAsync(id.Value, cancellationToken);
        var employee = await _db.Employees.SingleAsync(e => e.Id == id, cancellationToken);
        var now = _clock.UtcNow;
        var passwordOk = _hasher.Verify(employee.PasswordHash, request.Password);

        if (!employee.IsActive || employee.IsLockedOut(now) || !passwordOk)
        {
            if (employee.IsActive && !employee.IsLockedOut(now) && !passwordOk)
            {
                var lockedNow = employee.RecordFailedLogin(_options.MaxFailedLogins, TimeSpan.FromMinutes(_options.LockoutMinutes), now);
                Record(employee.Id, lockedNow ? "LockedOut" : "LoginFailed", new { employee.FailedLoginCount, employee.LockoutEndsAtUtc });
            }
            else
            {
                Record(employee.Id, "LoginRejected", new { Reason = employee.IsActive ? "locked" : "inactive" });
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthenticationFailedException();
        }

        employee.RecordSuccessfulLogin();
        var refresh = IssueRefresh(employee.Id, Guid.NewGuid(), now);
        Record(employee.Id, "LoggedIn", new { refresh.Token.FamilyId });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(employee, refresh);
    }

    /// <summary>Exchanges a refresh token for a new access token and a new refresh token (rotation).</summary>
    public async Task<AuthResult> RefreshAsync(string? presented, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presented) || presented.Length > 200)
        {
            throw new AuthenticationFailedException();
        }

        var hash = Hash(presented);
        var employeeId = await _db.RefreshTokens.AsNoTracking().Where(t => t.TokenHash == hash).Select(t => (int?)t.EmployeeId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new AuthenticationFailedException();

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _db.LockEmployeeAsync(employeeId, cancellationToken);
        var token = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == hash, cancellationToken);
        var employee = await _db.Employees.SingleAsync(e => e.Id == employeeId, cancellationToken);
        var now = _clock.UtcNow;

        if (token.WasRotated)
        {
            // A rotated token came back: someone kept a copy. End every session in its family.
            await RevokeFamilyAsync(token.FamilyId, "reuse detected", now, cancellationToken);
            Record(employee.Id, "RefreshTokenReuse", new { token.FamilyId });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthenticationFailedException();
        }

        if (!token.IsActive(now) || !employee.IsActive || employee.IsLockedOut(now))
        {
            token.Revoke(token.RevokedReason ?? (employee.IsActive ? "expired" : "inactive"), now);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthenticationFailedException();
        }

        var next = IssueRefresh(employee.Id, token.FamilyId, now);
        await _db.SaveChangesAsync(cancellationToken);
        token.ReplaceWith(next.Token, now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(employee, next);
    }

    /// <summary>Signs out the session the refresh token belongs to. Unknown or empty tokens are ignored.</summary>
    public async Task LogoutAsync(string? presented, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presented) || presented.Length > 200)
        {
            return;
        }

        var hash = Hash(presented);
        var token = await _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return;
        }

        await RevokeFamilyAsync(token.FamilyId, "signed out", _clock.UtcNow, cancellationToken);
        Record(token.EmployeeId, "LoggedOut", new { token.FamilyId });
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Admin: clears a lockout early.</summary>
    public async Task UnlockAsync(int employeeId, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        employee.Unlock();
        Record(employee.Id, "Unlocked", new { By = _currentUser.EmployeeId });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmployeeDto> CurrentAsync(CancellationToken cancellationToken)
    {
        var id = _currentUser.EmployeeId;
        var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException("Employee", id);
        return EmployeeDto.From(employee);
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTime now, CancellationToken cancellationToken)
    {
        var family = await _db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var t in family)
        {
            t.Revoke(reason, now);
        }
    }

    private (RefreshToken Token, string Plain) IssueRefresh(int employeeId, Guid familyId, DateTime now)
    {
        var plain = Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = new RefreshToken(employeeId, Hash(plain), familyId, now, now.AddDays(_options.RefreshTokenDays));
        _db.RefreshTokens.Add(token);
        return (token, plain);
    }

    private AuthResult Result(Employee employee, (RefreshToken Token, string Plain) refresh)
    {
        var access = _tokens.Issue(employee);
        return new AuthResult(new LoginResponse(access.AccessToken, access.ExpiresAtUtc, EmployeeDto.From(employee)), refresh.Plain, refresh.Token.ExpiresAtUtc);
    }

    private void Record(int employeeId, string action, object details) =>
        _audit.Record(AuditEntities.Employee, employeeId.ToString(CultureInfo.InvariantCulture), action, details);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
