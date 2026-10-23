using LedgerCore.Application.EventStore;

namespace LedgerCore.Application.Projections;

/// <summary>A read model built from the store's global order.</summary>
/// <remarks>Events can arrive again after a restart, so applying one twice must change nothing.</remarks>
public interface IProjection
{
    /// <summary>Names the projection's checkpoint, so keep it stable.</summary>
    string Name { get; }

    Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default);
}
