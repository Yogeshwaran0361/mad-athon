namespace ReworkGuard.Application.Abstractions;

/// <summary>Adds an audit event to the current unit of work so it commits atomically with the change it describes.</summary>
public interface IAuditWriter
{
    void Record(string entityType, string entityId, string action, object details);
}
