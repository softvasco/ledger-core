namespace LedgerCore.Application.Projections;

/// <summary>The last position each projection has applied.</summary>
/// <remarks>Keep it where the projection keeps its data, or a restart can skip or lose events.</remarks>
public interface ICheckpointStore
{
    /// <summary>0 when the projection has never run.</summary>
    Task<long> GetAsync(string projection, CancellationToken cancellationToken = default);

    Task SaveAsync(string projection, long position, CancellationToken cancellationToken = default);
}
