using LedgerCore.Application.EventStore;

namespace LedgerCore.Application.Snapshots;

/// <summary>Latest snapshot per stream. Only a cache: the events alone must give the same state.</summary>
public interface ISnapshotStore<TSnapshot>
    where TSnapshot : class
{
    Task<TSnapshot?> LoadAsync(StreamId stream, CancellationToken cancellationToken = default);

    /// <summary>Keeps <paramref name="snapshot"/> unless the stored one is from a later version.</summary>
    Task SaveAsync(StreamId stream, long version, TSnapshot snapshot, CancellationToken cancellationToken = default);
}
