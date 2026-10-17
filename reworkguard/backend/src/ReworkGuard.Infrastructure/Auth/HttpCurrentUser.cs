using System.Globalization;
using Microsoft.AspNetCore.Http;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Infrastructure.Auth;

internal sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public int EmployeeId =>
        int.Parse(RequiredClaim(ClaimNames.Subject), NumberStyles.None, CultureInfo.InvariantCulture);

    public Role Role => Enum.Parse<Role>(RequiredClaim(ClaimNames.Role));

    private string RequiredClaim(string type)
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("No authenticated user in the current request.");
        }

        return user.FindFirst(type)?.Value
            ?? throw new InvalidOperationException($"The access token has no '{type}' claim.");
    }
}
