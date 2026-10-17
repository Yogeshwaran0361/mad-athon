using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Application.Employees;

public sealed record EmployeeDto(int Id, string Code, string Name, string Email, Role Role, PayMode PayMode, bool IsActive)
{
    public static EmployeeDto From(Employee e) => new(e.Id, e.Code, e.Name, e.Email, e.Role, e.PayMode, e.IsActive);
}
