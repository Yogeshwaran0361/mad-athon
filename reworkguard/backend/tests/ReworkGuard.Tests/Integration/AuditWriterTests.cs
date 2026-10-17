using Microsoft.EntityFrameworkCore;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Persistence;
using ReworkGuard.Infrastructure.Services;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

[Collection(MySqlTestGroup.Name)]
public sealed class AuditWriterTests : IAsyncLifetime
{
    private readonly MySqlContainerFixture _mysql;
    private ApplicationDbContext _db = null!;

    public AuditWriterTests(MySqlContainerFixture mysql)
    {
        _mysql = mysql;
    }

    public async Task InitializeAsync() => _db = MySqlContainerFixture.CreateContext(await _mysql.CreateMigratedDatabaseAsync());

    public Task DisposeAsync() => MySqlContainerFixture.DropDatabaseAsync(_db);

    [Fact]
    public async Task Records_actor_time_and_json_details_on_save()
    {
        var actor = await new TestData(_db).EmployeeAsync(Role.Manager);
        var user = new TestCurrentUser();
        user.SignInAs(actor);
        var clock = new TestClock();
        var writer = new AuditWriter(_db, user, clock);

        writer.Record("Bug", "42", "Reviewed", new { RootCause = RootCause.OwnDefect, Deductible = true });
        await _db.SaveChangesAsync();

        var saved = await _db.AuditEvents.AsNoTracking().SingleAsync(a => a.Action == "Reviewed");
        saved.ActorEmployeeId.Should().Be(actor.Id);
        saved.OccurredAtUtc.Should().Be(clock.UtcNow);
        saved.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        saved.EntityType.Should().Be("Bug");
        saved.EntityId.Should().Be("42");
        saved.DetailsJson.Should().Contain("\"rootCause\": \"OwnDefect\"").And.Contain("\"deductible\": true");
    }

    [Fact]
    public async Task Background_jobs_are_recorded_as_the_system()
    {
        new AuditWriter(_db, new TestCurrentUser(), new TestClock()).Record("Deduction", "7", "DisputeWindowExpired", new { });
        await _db.SaveChangesAsync();

        (await _db.AuditEvents.AsNoTracking().SingleAsync()).ActorEmployeeId.Should().BeNull();
    }

    [Fact]
    public async Task The_context_refuses_to_modify_or_delete_audit_events()
    {
        var actor = await new TestData(_db).EmployeeAsync(Role.Manager);
        var user = new TestCurrentUser();
        user.SignInAs(actor);
        new AuditWriter(_db, user, new TestClock()).Record("Task", "1", "Started", new { });
        await _db.SaveChangesAsync();

        var saved = await _db.AuditEvents.SingleAsync();
        _db.AuditEvents.Remove(saved);
        var act = () => _db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
    }
}
