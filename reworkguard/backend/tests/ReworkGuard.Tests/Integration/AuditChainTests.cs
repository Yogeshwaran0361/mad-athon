using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using ReworkGuard.Application.Audit;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Persistence;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>The audit hash chain: how it is built, what verification catches, export, and least-privilege grants.</summary>
public sealed class AuditChainTests : ApiTestBase
{
    public AuditChainTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Business_actions_extend_a_contiguous_verified_chain()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        await s.DeductibleBugAsync(hours: 2m);
        var auditor = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));

        var chain = await FreshDb().AuditEvents.AsNoTracking().OrderBy(a => a.Sequence).ToListAsync();
        chain.Should().HaveCountGreaterThan(3);
        chain.Select(a => a.Sequence).Should().Equal(Enumerable.Range(1, chain.Count).Select(i => (long)i));
        chain[0].PrevHash.Should().Be(AuditChainSql.Genesis);
        chain.Zip(chain.Skip(1)).Should().OnlyContain(p => p.Second.PrevHash == p.First.Hash);
        chain.Should().OnlyContain(a => a.Hash.Length == 64);

        var result = await (await auditor.GetAsync(new Uri("/api/v1/audit/verify", UriKind.Relative))).ReadAsync<AuditVerificationDto>();
        result.Should().BeEquivalentTo(new { Valid = true, CheckedCount = (long)chain.Count, HeadSequence = (long)chain.Count, HeadHash = chain[^1].Hash, FirstBrokenSequence = (long?)null });
        (await FreshDb().AuditEvents.CountAsync(a => a.EntityType == "AuditLog" && a.Action == "Verified")).Should().Be(1, "verification is itself audited");
    }

    [Theory]
    [InlineData("edit", "hash")]
    [InlineData("delete-middle", "gap")]
    [InlineData("delete-last", "head")]
    [InlineData("reorder", "hash")]
    public async Task Verification_finds_the_first_tampered_entry(string tampering, string reason)
    {
        for (var i = 0; i < 5; i++)
        {
            Db.AuditEvents.Add(new AuditEvent(Clock.UtcNow.AddMinutes(i), null, "Task", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Started", $"{{\"n\":{i}}}"));
            await Db.SaveChangesAsync();
        }

        // A DBA with full rights can drop the guards; the chain still exposes the change.
        await Db.Database.ExecuteSqlRawAsync("DROP TRIGGER `TR_AuditEvents_NoUpdate`");
        await Db.Database.ExecuteSqlRawAsync("DROP TRIGGER `TR_AuditEvents_NoDelete`");
        var expected = tampering switch
        {
            "edit" => 3L,
            "delete-middle" => 4L,
            "delete-last" => 5L,
            _ => 2L,
        };
        var sql = tampering switch
        {
            "edit" => "UPDATE AuditEvents SET DetailsJson = JSON_OBJECT('n', 99) WHERE Sequence = 3",
            "delete-middle" => "DELETE FROM AuditEvents WHERE Sequence = 3",
            "delete-last" => "DELETE FROM AuditEvents WHERE Sequence = 5",
            _ => "UPDATE AuditEvents SET OccurredAtUtc = DATE_ADD(OccurredAtUtc, INTERVAL 1 DAY) WHERE Sequence = 2",
        };
        await Db.Database.ExecuteSqlRawAsync(sql);

        var result = await Api.RunScopedAsync<Application.Abstractions.IAuditChainVerifier, AuditVerificationDto>(v => v.VerifyAsync(CancellationToken.None));

        result.Valid.Should().BeFalse();
        result.FirstBrokenSequence.Should().Be(expected);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task Concurrent_writers_are_chained_without_forks()
    {
        var connection = Db.Database.GetConnectionString()!;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await using var db = MySqlContainerFixture.CreateContext(connection);
            await using var tx = await db.Database.BeginTransactionAsync();
            db.AuditEvents.Add(new AuditEvent(DateTime.UtcNow, null, "Task", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Started", "{}"));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }));

        var sequences = await FreshDb().AuditEvents.Select(a => a.Sequence).OrderBy(x => x).ToListAsync();
        sequences.Should().Equal(Enumerable.Range(1, 20).Select(i => (long)i));
        (await Api.RunScopedAsync<Application.Abstractions.IAuditChainVerifier, AuditVerificationDto>(v => v.VerifyAsync(CancellationToken.None))).Valid.Should().BeTrue();
    }

    [Fact]
    public async Task The_migration_chains_entries_written_before_it()
    {
        var name = $"rg_chain_{Guid.NewGuid():N}"[..30];
        var connection = new MySqlConnectionStringBuilder(Db.Database.GetConnectionString()!) { Database = name }.ConnectionString;
        await using var db = MySqlContainerFixture.CreateContext(connection);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("PayrollRuns");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO AuditEvents (OccurredAtUtc, EntityType, EntityId, Action, DetailsJson) VALUES "
            + "('2026-01-01 10:00:00.123456', 'Task', '1', 'Started', '{{\"b\": 2, \"a\": 1}}'), ('2026-01-02 10:00:00', 'Task', '1', 'Completed', '{{}}')");

        await migrator.MigrateAsync();

        var rows = await db.AuditEvents.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Sequence, a.PrevHash, a.Hash }).ToListAsync();
        rows.Select(r => r.Sequence).Should().Equal(1L, 2L);
        rows[1].PrevHash.Should().Be(rows[0].Hash);
        db.AuditEvents.Add(new AuditEvent(DateTime.UtcNow, null, "Task", "1", "Closed", "{}"));
        await db.SaveChangesAsync();
        var verifier = new AuditChainVerifier(db, Clock);
        (await verifier.VerifyAsync(CancellationToken.None)).Should().BeEquivalentTo(new { Valid = true, CheckedCount = 3L, HeadSequence = 3L });
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Search_and_csv_export_are_for_auditors_and_neutralise_formulas()
    {
        var admin = await Data.EmployeeAsync(Role.Admin);
        var employee = await Data.EmployeeAsync(Role.Employee);
        Db.AuditEvents.Add(new AuditEvent(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc), admin.Id, "Policy", "=HYPERLINK(\"x\")", "Updated", "{\"note\":\"a,b\"}"));
        Db.AuditEvents.Add(new AuditEvent(new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc), null, "Task", "7", "Closed", "{}"));
        await Db.SaveChangesAsync();
        var auditor = await Api.ClientForAsync(admin);

        var page = await (await auditor.GetAsync(new Uri($"/api/v1/audit-events?actorEmployeeId={admin.Id}&action=Updated", UriKind.Relative))).ReadAsync<PagedResult<AuditEventDto>>();
        page.Items.Should().ContainSingle().Which.ActorName.Should().Be(admin.Name);
        var dated = await (await auditor.GetAsync(new Uri("/api/v1/audit-events?fromUtc=2026-03-04T00:00:00Z&toUtc=2026-03-06T00:00:00Z", UriKind.Relative))).ReadAsync<PagedResult<AuditEventDto>>();
        dated.Items.Should().ContainSingle().Which.Should().Match<AuditEventDto>(a => a.ActorName == "System" && a.EntityId == "7");
        await (await auditor.GetAsync(new Uri("/api/v1/audit-events?fromUtc=2026-03-06T00:00:00Z&toUtc=2026-03-01T00:00:00Z", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest);
        await (await (await Api.ClientForAsync(employee)).GetAsync(new Uri("/api/v1/audit-events/export.csv", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await (await Api.ClientForAsync(employee)).GetAsync(new Uri("/api/v1/audit/verify", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);

        var response = await auditor.GetAsync(new Uri("/api/v1/audit-events/export.csv?entityType=Policy", UriKind.Relative));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Sequence,Id,OccurredAtUtc,ActorEmployeeId,ActorName,EntityType,EntityId,Action,DetailsJson,Hash");
        lines.Should().HaveCount(2);
        lines[1].Should().Contain($",{admin.Id},{admin.Name},Policy,\"'=HYPERLINK(\"\"x\"\")\",Updated,");
        lines[1].Should().Contain("2026-03-01T08:00:00.000000Z");
        (await FreshDb().AuditEvents.CountAsync(a => a.EntityType == "AuditLog" && a.Action == "Exported")).Should().Be(1);
    }

    [Fact]
    public async Task The_least_privilege_account_cannot_change_append_only_tables()
    {
        var root = Db.Database.GetConnectionString()!;
        var user = $"rg_app_{Guid.NewGuid():N}"[..16];
        await using (var admin = new MySqlConnection(root))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"CREATE USER '{user}'@'%' IDENTIFIED BY 'test-only-app-pw'");
            foreach (var statement in SplitScript(await File.ReadAllTextAsync(RepoFile("scripts/db/app-user-grants.sql"))))
            {
                await ExecuteAsync(admin, statement);
            }

            await ExecuteAsync(admin, $"CALL rg_grant_app_user('{user}', '%')");
            await ExecuteAsync(admin, "DROP PROCEDURE rg_grant_app_user");
        }

        var appConnection = new MySqlConnectionStringBuilder(root) { UserID = user, Password = "test-only-app-pw" }.ConnectionString;
        await using var app = new MySqlConnection(appConnection);
        await app.OpenAsync();

        await ExecuteAsync(app, "INSERT INTO AuditEvents (OccurredAtUtc, EntityType, EntityId, Action, DetailsJson) VALUES (UTC_TIMESTAMP(6), 'Task', '1', 'Started', '{}')");
        await ExecuteAsync(app, "UPDATE PolicySettings SET Value = Value WHERE `Key` = 'DailyHours'");
        foreach (var sql in new[]
                 {
                     "UPDATE AuditEvents SET Action = 'x'", "DELETE FROM AuditEvents", "UPDATE AuditChainHead SET LastSequence = 0",
                     "DELETE FROM TaskStateHistory", "UPDATE PayrollRunItems SET Applied = 0", "DROP TRIGGER TR_AuditEvents_Chain", "ALTER TABLE Employees ADD COLUMN x INT",
                 })
        {
            var act = () => ExecuteAsync(app, sql);
            (await act.Should().ThrowAsync<MySqlException>(sql)).Which.Number.Should().BeOneOf(1044, 1142, 1227, 1419);
        }

        (await Api.RunScopedAsync<Application.Abstractions.IAuditChainVerifier, AuditVerificationDto>(v => v.VerifyAsync(CancellationToken.None))).Valid.Should().BeTrue();
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Splits a mysql-client script into statements, honouring DELIMITER directives and skipping comments.</summary>
    private static IEnumerable<string> SplitScript(string script)
    {
        var delimiter = ";";
        var current = new StringBuilder();
        foreach (var raw in script.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("--", StringComparison.Ordinal) || (current.Length == 0 && line.Trim().Length == 0))
            {
                continue;
            }

            if (line.StartsWith("DELIMITER ", StringComparison.Ordinal))
            {
                delimiter = line["DELIMITER ".Length..].Trim();
                continue;
            }

            current.AppendLine(line);
            var text = current.ToString().TrimEnd();
            if (text.EndsWith(delimiter, StringComparison.Ordinal))
            {
                yield return text[..^delimiter.Length];
                current.Clear();
            }
        }
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative)))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new FileNotFoundException(relative), relative);
    }
}
