using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Abstractions;

/// <summary>The signed-in user. Background jobs have none: <see cref="IsAuthenticated"/> is false and they act as the system.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    int EmployeeId { get; }

    Role Role { get; }
}
