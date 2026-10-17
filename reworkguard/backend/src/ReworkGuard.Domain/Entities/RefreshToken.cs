namespace ReworkGuard.Domain.Entities;

/// <summary>
/// A refresh token, stored only as its SHA-256 hash. Each use rotates it: the old token is revoked and replaced by a
/// new one in the same family. Presenting a token that was already rotated means it was copied, so the whole family is
/// revoked and the holder must sign in again.
/// </summary>
public class RefreshToken
{
    public const string RotatedReason = "rotated";

    private RefreshToken()
    {
    }

    public RefreshToken(int employeeId, string tokenHash, Guid familyId, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (tokenHash.Length != 64)
        {
            throw new ArgumentException("A SHA-256 hex hash is required.", nameof(tokenHash));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("A refresh token must expire after it is created.", nameof(expiresAtUtc));
        }

        EmployeeId = employeeId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public long Id { get; private set; }

    public int EmployeeId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid FamilyId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public string? RevokedReason { get; private set; }

    public long? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public bool WasRotated => RevokedReason == RotatedReason;

    public void Revoke(string reason, DateTime utcNow)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = utcNow;
        RevokedReason = reason;
    }

    public void ReplaceWith(RefreshToken next, DateTime utcNow)
    {
        Revoke(RotatedReason, utcNow);
        ReplacedByTokenId = next.Id;
    }
}
