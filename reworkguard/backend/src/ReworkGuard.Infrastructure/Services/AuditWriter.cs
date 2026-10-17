using System.Text.Json;
using System.Text.Json.Serialization;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Infrastructure.Services;

internal sealed class AuditWriter : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AuditWriter(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public void Record(string entityType, string entityId, string action, object details)
    {
        _db.AuditEvents.Add(new AuditEvent(
            _clock.UtcNow,
            // Background jobs run without a user; their events are recorded with no actor, i.e. the system.
            _currentUser.IsAuthenticated ? _currentUser.EmployeeId : null,
            entityType,
            entityId,
            action,
            JsonSerializer.Serialize(details, JsonOptions)));
    }
}
