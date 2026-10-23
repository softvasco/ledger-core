using LedgerCore.Application.EventStore;

namespace LedgerCore.Application.Projections;

/// <summary>Feeds each projection the events stored after its checkpoint.</summary>
public sealed class ProjectionRunner : IDisposable
{
    private readonly IEventStore _events;
    private readonly ICheckpointStore _checkpoints;
    private readonly IReadOnlyList<IProjection> _projections;
    private readonly int _checkpointEvery;
    private readonly SemaphoreSlim _running = new(1, 1);

    public ProjectionRunner(
        IEventStore events,
        ICheckpointStore checkpoints,
        IEnumerable<IProjection> projections,
        int checkpointEvery = 100)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(projections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkpointEvery);

        _events = events;
        _checkpoints = checkpoints;
        _projections = [.. projections];
        _checkpointEvery = checkpointEvery;

        var duplicate = _projections.GroupBy(p => p.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Two projections share the checkpoint name '{duplicate.Key}'.", nameof(projections));
        }
    }

    /// <summary>Brings every projection up to the end of the store and returns how many events they applied.</summary>
    public async Task<int> CatchUpAsync(CancellationToken cancellationToken = default)
    {
        // one pass at a time, or two passes could apply the same events and race on the checkpoint
        await _running.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var applied = 0;
            foreach (var projection in _projections)
            {
                applied += await CatchUpAsync(projection, cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        finally
        {
            _running.Release();
        }
    }

    public void Dispose() => _running.Dispose();

    private async Task<int> CatchUpAsync(IProjection projection, CancellationToken cancellationToken)
    {
        var saved = await _checkpoints.GetAsync(projection.Name, cancellationToken).ConfigureAwait(false);
        var last = saved;
        var applied = 0;
        try
        {
            await foreach (var recorded in _events.ReadAllAsync(saved, cancellationToken).ConfigureAwait(false))
            {
                await projection.ApplyAsync(recorded, cancellationToken).ConfigureAwait(false);
                last = recorded.Position;
                applied++;

                if (applied % _checkpointEvery == 0)
                {
                    await _checkpoints.SaveAsync(projection.Name, last, cancellationToken).ConfigureAwait(false);
                    saved = last;
                }
            }
        }
        finally
        {
            // keep what was applied before a failure, so the retry starts at the event that broke
            if (last > saved)
            {
                await _checkpoints.SaveAsync(projection.Name, last, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return applied;
    }
}
