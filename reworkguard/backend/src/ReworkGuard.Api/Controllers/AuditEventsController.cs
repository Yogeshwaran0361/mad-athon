using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReworkGuard.Api.Authorization;
using ReworkGuard.Api.Infrastructure;
using ReworkGuard.Application.Audit;
using ReworkGuard.Application.Common;

namespace ReworkGuard.Api.Controllers;

/// <summary>Read-only view of the audit trail: search, CSV export and hash-chain verification. There are deliberately no write endpoints.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/audit-events")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AuditEventsController : ControllerBase
{
    private readonly AuditQueryService _audit;

    public AuditEventsController(AuditQueryService audit)
    {
        _audit = audit;
    }

    /// <summary>Newest first.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Auditors)]
    public Task<PagedResult<AuditEventDto>> List([FromQuery] AuditFilter filter, [FromQuery] PagingQuery paging, CancellationToken cancellationToken) =>
        _audit.ListAsync(filter.ToQuery(), paging.ToPageRequest(), cancellationToken);

    /// <summary>The matching entries as CSV, oldest first (up to 100,000). The export itself is audited.</summary>
    [HttpGet("export.csv")]
    [Authorize(Roles = Roles.Auditors)]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] AuditFilter filter, CancellationToken cancellationToken)
    {
        var (content, fileName, _) = await _audit.ExportCsvAsync(filter.ToQuery(), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(content, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>Re-computes every entry's hash and link; reports the first entry that was altered, removed or reordered.</summary>
    [HttpGet("~/" + ApiRoutes.V1 + "/audit/verify")]
    [Authorize(Roles = Roles.Auditors)]
    public Task<AuditVerificationDto> Verify(CancellationToken cancellationToken) => _audit.VerifyAsync(cancellationToken);

    public sealed record AuditFilter(string? EntityType, string? EntityId, int? ActorEmployeeId, string? Action, DateTime? FromUtc, DateTime? ToUtc)
    {
        public AuditEventQuery ToQuery() => new(EntityType, EntityId, ActorEmployeeId, Action, FromUtc, ToUtc);
    }
}
