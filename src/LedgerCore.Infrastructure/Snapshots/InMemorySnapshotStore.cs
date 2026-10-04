using System.Collections.Concurrent;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;

namespace LedgerCore.Infrastructure.Snapshots;

/// <summary>Keeps the latest snapshot per stream in memory. For tests and local runs.</summary>
public sealed class InMemorySnapshotStore<TSnapshot> : ISnapshotStore<TSnapshot>
    where TSnapshot : class
{
    private readonly ConcurrentDictionary<StreamId, (long Version, TSnapshot Snapshot)> _latest = new();

    public Task<TSnapshot?> LoadAsync(StreamId stream, CancellationToken cancellationToken = default) =>
        Task.FromResult(_latest.TryGetValue(stream, out var entry) ? entry.Snapshot : null);

    public Task SaveAsync(StreamId stream, long version, TSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(snapshot);

        _latest.AddOrUpdate(
            stream,
            (version, snapshot),
            (_, stored) => version > stored.Version ? (version, snapshot) : stored);
        return Task.CompletedTask;
    }

    public void Clear() => _latest.Clear();
}
