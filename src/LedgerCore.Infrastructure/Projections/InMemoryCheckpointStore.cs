using System.Collections.Concurrent;
using LedgerCore.Application.Projections;

namespace LedgerCore.Infrastructure.Projections;

/// <summary>Checkpoints for projections that also live in memory.</summary>
public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, long> _positions = new();

    public Task<long> GetAsync(string projection, CancellationToken cancellationToken = default) =>
        Task.FromResult(_positions.GetValueOrDefault(projection));

    public Task SaveAsync(string projection, long position, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        _positions[projection] = position;
        return Task.CompletedTask;
    }
}
