namespace LedgerCore.Application.Projections;

/// <summary>A projection that saves its checkpoint itself, in the same transaction as the rows an event changed.</summary>
/// <remarks>The runner reads the checkpoint from it and never saves one for it.</remarks>
public interface ICheckpointedProjection : IProjection
{
    Task<long> GetCheckpointAsync(CancellationToken cancellationToken = default);
}
