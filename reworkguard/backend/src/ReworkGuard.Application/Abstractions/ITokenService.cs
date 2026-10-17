using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Abstractions;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

public interface ITokenService
{
    IssuedToken Issue(Employee employee);
}
