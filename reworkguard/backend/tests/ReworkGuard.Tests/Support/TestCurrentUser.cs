using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Tests.Support;

public sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => EmployeeId != 0;

    public int EmployeeId { get; private set; }

    public Role Role { get; private set; }

    public void SignInAs(Employee employee)
    {
        EmployeeId = employee.Id;
        Role = employee.Role;
    }
}
