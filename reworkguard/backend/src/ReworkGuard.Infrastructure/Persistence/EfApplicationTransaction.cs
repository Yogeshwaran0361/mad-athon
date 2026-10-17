using Microsoft.EntityFrameworkCore.Storage;
using ReworkGuard.Application.Abstractions;

namespace ReworkGuard.Infrastructure.Persistence;

internal sealed class EfApplicationTransaction : IApplicationTransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfApplicationTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
