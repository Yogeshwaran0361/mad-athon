using System.Globalization;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Common;
using ReworkGuard.Domain.Entities;

namespace ReworkGuard.Application.Audit;

/// <summary>Search, export and verification of the audit trail. Read-only by design.</summary>
public sealed class AuditQueryService
{
    /// <summary>Exports are capped so one request cannot pull an unbounded table; narrow the filters for more.</summary>
    public const int MaxExportRows = 100_000;

    private readonly IApplicationDbContext _db;
    private readonly IAuditChainVerifier _verifier;
    private readonly IAuditWriter _audit;
    private readonly ICurrentUser _user;

    public AuditQueryService(IApplicationDbContext db, IAuditChainVerifier verifier, IAuditWriter audit, ICurrentUser user)
    {
        _db = db;
        _verifier = verifier;
        _audit = audit;
        _user = user;
    }

    public Task<PagedResult<AuditEventDto>> ListAsync(AuditEventQuery filter, PageRequest page, CancellationToken cancellationToken) =>
        Project(Filter(filter).OrderByDescending(a => a.Sequence)).ToPagedResultAsync(page, cancellationToken);

    /// <summary>The matching entries, oldest first, as RFC 4180 CSV. Spreadsheet formulas in values are neutralised.</summary>
    public async Task<(byte[] Content, string FileName, int Rows)> ExportCsvAsync(AuditEventQuery filter, CancellationToken cancellationToken)
    {
        var rows = await Project(Filter(filter).OrderBy(a => a.Sequence)).Take(MaxExportRows + 1).ToListAsync(cancellationToken);
        if (rows.Count > MaxExportRows)
        {
            throw new ValidationException([new ValidationFailure("filter", $"More than {MaxExportRows:N0} entries match; narrow the date range or filters.")]);
        }

        var csv = new StringBuilder("Sequence,Id,OccurredAtUtc,ActorEmployeeId,ActorName,EntityType,EntityId,Action,DetailsJson,Hash\r\n");
        foreach (var r in rows)
        {
            csv.AppendJoin(',', new[]
            {
                r.Sequence.ToString(CultureInfo.InvariantCulture),
                r.Id.ToString(CultureInfo.InvariantCulture),
                r.OccurredAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
                r.ActorEmployeeId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                r.ActorName,
                r.EntityType,
                r.EntityId,
                r.Action,
                r.DetailsJson,
                r.Hash,
            }.Select(Field)).Append("\r\n");
        }

        _audit.Record(AuditEntities.AuditLog, "export", "Exported", new { filter.EntityType, filter.EntityId, filter.ActorEmployeeId, filter.Action, filter.FromUtc, filter.ToUtc, Rows = rows.Count });
        await _db.SaveChangesAsync(cancellationToken);
        return (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), $"audit_{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}.csv", rows.Count);
    }

    public async Task<AuditVerificationDto> VerifyAsync(CancellationToken cancellationToken)
    {
        var result = await _verifier.VerifyAsync(cancellationToken);
        _audit.Record(AuditEntities.AuditLog, "chain", "Verified", new { result.Valid, result.CheckedCount, result.HeadSequence, result.FirstBrokenSequence, result.Reason, By = _user.EmployeeId });
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>Quotes a CSV field and prefixes values a spreadsheet would run as a formula.</summary>
    internal static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private IQueryable<AuditEvent> Filter(AuditEventQuery f)
    {
        if (f.FromUtc is { } from && f.ToUtc is { } to && from > to)
        {
            throw new ValidationException([new ValidationFailure("fromUtc", "fromUtc must not be after toUtc.")]);
        }

        var query = _db.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.EntityType))
        {
            query = query.Where(a => a.EntityType == f.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(f.EntityId))
        {
            query = query.Where(a => a.EntityId == f.EntityId);
        }

        if (f.ActorEmployeeId is { } actor)
        {
            query = query.Where(a => a.ActorEmployeeId == actor);
        }

        if (!string.IsNullOrWhiteSpace(f.Action))
        {
            query = query.Where(a => a.Action == f.Action);
        }

        if (f.FromUtc is { } fromUtc)
        {
            query = query.Where(a => a.OccurredAtUtc >= fromUtc);
        }

        if (f.ToUtc is { } toUtc)
        {
            query = query.Where(a => a.OccurredAtUtc <= toUtc);
        }

        return query;
    }

    private IQueryable<AuditEventDto> Project(IQueryable<AuditEvent> query) =>
        from a in query
        join e in _db.Employees on a.ActorEmployeeId equals e.Id into actors
        from actor in actors.DefaultIfEmpty()
        select new AuditEventDto(a.Id, a.Sequence, a.OccurredAtUtc, a.ActorEmployeeId, actor == null ? "System" : actor.Name, a.EntityType, a.EntityId, a.Action, a.DetailsJson, a.Hash);
}
