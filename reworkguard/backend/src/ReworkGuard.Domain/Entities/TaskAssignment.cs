namespace ReworkGuard.Domain.Entities;

public class TaskAssignment
{
    private TaskAssignment()
    {
    }

    internal TaskAssignment(int employeeId, bool isOriginalDeliverer)
    {
        EmployeeId = employeeId;
        IsOriginalDeliverer = isOriginalDeliverer;
    }

    public int Id { get; private set; }

    public int TaskId { get; private set; }

    public int EmployeeId { get; private set; }

    public Employee Employee { get; private set; } = null!;

    public bool IsOriginalDeliverer { get; private set; }
}
