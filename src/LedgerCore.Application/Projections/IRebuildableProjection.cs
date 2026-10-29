namespace LedgerCore.Application.Projections;

/// <summary>A projection that can be emptied and built again from the first event.</summary>
public interface IRebuildableProjection : IProjection
{
    /// <summary>Removes everything it built, including its own checkpoint if it keeps one.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Events a rebuild has applied so far, and the global position of the last one.</summary>
public sealed record RebuildProgress(string Projection, int Applied, long Position);
