using Microsoft.AspNetCore.Identity;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Auth;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (HMAC-SHA512, 100k iterations, per-password salt).</summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<Employee> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hashedPassword, string providedPassword) =>
        _hasher.VerifyHashedPassword(null!, hashedPassword, providedPassword) != PasswordVerificationResult.Failed;
}
