namespace ReworkGuard.Domain.Common;

/// <summary>Entities carrying an optimistic concurrency token that is bumped on every update.</summary>
public interface IVersioned
{
    int Version { get; set; }
}
