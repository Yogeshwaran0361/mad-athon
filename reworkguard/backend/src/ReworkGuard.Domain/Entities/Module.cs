namespace ReworkGuard.Domain.Entities;

public class Module
{
    private Module()
    {
    }

    public Module(Project project, string name)
    {
        Project = project;
        Name = name;
    }

    public int Id { get; private set; }

    public int ProjectId { get; private set; }

    public Project Project { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;
}
