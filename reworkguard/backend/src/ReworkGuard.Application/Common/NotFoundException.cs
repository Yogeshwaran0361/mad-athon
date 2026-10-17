namespace ReworkGuard.Application.Common;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, object id)
        : base($"{entity} {id} was not found.")
    {
    }
}
