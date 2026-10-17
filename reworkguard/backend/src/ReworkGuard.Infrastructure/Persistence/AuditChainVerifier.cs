using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Audit;

namespace ReworkGuard.Infrastructure.Persistence;

internal sealed class AuditChainVerifier : IAuditChainVerifier
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public AuditChainVerifier(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AuditVerificationDto> VerifyAsync(CancellationToken cancellationToken)
    {
        // Read the head first: entries committed after it are not judged against it.
        var head = await _db.Database.SqlQueryRaw<HeadRow>("SELECT `LastSequence`, `LastHash` FROM `AuditChainHead` WHERE `Id` = 1").SingleAsync(cancellationToken);
        var count = await _db.AuditEvents.LongCountAsync(a => a.Sequence <= head.LastSequence, cancellationToken);
        var breaks = await _db.Database.SqlQueryRaw<BreakRow>(AuditChainSql.FirstBreak).ToListAsync(cancellationToken);
        var first = breaks.FirstOrDefault(b => b.Sequence <= head.LastSequence);
        if (first is null)
        {
            var last = await _db.AuditEvents.AsNoTracking().Where(a => a.Sequence == head.LastSequence).Select(a => new { a.Id, a.Hash }).SingleOrDefaultAsync(cancellationToken);
            if (head.LastSequence > 0 && (last is null || last.Hash != head.LastHash))
            {
                return new AuditVerificationDto(false, count, head.LastSequence, head.LastHash, head.LastSequence, last?.Id, "head", _clock.UtcNow);
            }
        }

        return new AuditVerificationDto(first is null, count, head.LastSequence, head.LastHash, first?.Sequence, first?.Id, first?.Reason, _clock.UtcNow);
    }

    private sealed record HeadRow(long LastSequence, string LastHash);

    private sealed record BreakRow(long Sequence, long Id, string Reason);
}
