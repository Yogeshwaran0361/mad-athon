using ReworkGuard.Application.Audit;

namespace ReworkGuard.Application.Abstractions;

/// <summary>Re-computes the audit hash chain in the database and reports the first entry that does not match.</summary>
public interface IAuditChainVerifier
{
    Task<AuditVerificationDto> VerifyAsync(CancellationToken cancellationToken);
}
