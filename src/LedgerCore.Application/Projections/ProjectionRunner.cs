using LedgerCore.Application.Diagnostics;
using LedgerCore.Application.EventStore;

namespace LedgerCore.Application.Projections;

/// <summary>Feeds each projection the events stored after its checkpoint.</summary>
public sealed class ProjectionRunner : IDisposable
{
    private readonly IEventStore _events;
    private readonly ICheckpointStore _checkpoints;
    private readonly IReadOnlyList<IProjection> _projections;
    private readonly int _checkpointEvery;
    private readonly LedgerMetrics? _metrics;
    private readonly SemaphoreSlim _running = new(1, 1);

    public ProjectionRunner(
        IEventStore events,
        ICheckpointStore checkpoints,
        IEnumerable<IProjection> projections,
        int checkpointEvery = 100,
        LedgerMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(projections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(checkpointEvery);

        _events = events;
        _checkpoints = checkpoints;
        _projections = [.. projections];
        _checkpointEvery = checkpointEvery;
        _metrics = metrics;

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
                applied += await CatchUpAsync(projection, null, measureLag: true, cancellationToken).ConfigureAwait(false);
            }

            return applied;
        }
        finally
        {
            _running.Release();
        }
    }

    public IReadOnlyList<string> ProjectionNames => [.. _projections.Select(p => p.Name)];

    /// <summary>Empties one projection and applies every stored event to it again.</summary>
    /// <exception cref="ArgumentException">No projection has that name.</exception>
    /// <exception cref="InvalidOperationException">The projection can't be emptied.</exception>
    public async Task<int> RebuildAsync(
        string projection,
        IProgress<RebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = _projections.FirstOrDefault(p => p.Name == projection)
            ?? throw new ArgumentException($"No projection is named '{projection}'.", nameof(projection));
        if (target is not IRebuildableProjection rebuildable)
        {
            throw new InvalidOperationException($"Projection '{projection}' can't be emptied, so it can't be rebuilt.");
        }

        // same lock as catch-up, so the background pass can't write into a half-empty model
        await _running.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await rebuildable.ResetAsync(cancellationToken).ConfigureAwait(false);
            if (target is not ICheckpointedProjection)
            {
                await _checkpoints.SaveAsync(target.Name, 0, cancellationToken).ConfigureAwait(false);
            }

            // a rebuild replays old events, so their age says nothing about lag
            return await CatchUpAsync(target, progress, measureLag: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _running.Release();
        }
    }

    public void Dispose() => _running.Dispose();

    private async Task<int> CatchUpAsync(
        IProjection projection,
        IProgress<RebuildProgress>? progress,
        bool measureLag,
        CancellationToken cancellationToken)
    {
        if (projection is ICheckpointedProjection checkpointed)
        {
            return await CatchUpAsync(checkpointed, progress, measureLag, cancellationToken).ConfigureAwait(false);
        }

        var saved = await _checkpoints.GetAsync(projection.Name, cancellationToken).ConfigureAwait(false);
        var last = saved;
        var applied = 0;
        try
        {
            await foreach (var recorded in _events.ReadAllAsync(saved, cancellationToken).ConfigureAwait(false))
            {
                await projection.ApplyAsync(recorded, cancellationToken).ConfigureAwait(false);
                if (measureLag)
                {
                    _metrics?.ProjectionApplied(projection.Name, recorded.Event);
                }

                last = recorded.Position;
                applied++;

                if (applied % _checkpointEvery == 0)
                {
                    await _checkpoints.SaveAsync(projection.Name, last, cancellationToken).ConfigureAwait(false);
                    saved = last;
                    progress?.Report(new RebuildProgress(projection.Name, applied, last));
                }
            }

            ReportEnd(progress, projection.Name, applied, last);
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

    private async Task<int> CatchUpAsync(
        ICheckpointedProjection projection,
        IProgress<RebuildProgress>? progress,
        bool measureLag,
        CancellationToken cancellationToken)
    {
        var from = await projection.GetCheckpointAsync(cancellationToken).ConfigureAwait(false);
        var last = from;
        var applied = 0;
        await foreach (var recorded in _events.ReadAllAsync(from, cancellationToken).ConfigureAwait(false))
        {
            await projection.ApplyAsync(recorded, cancellationToken).ConfigureAwait(false);
            if (measureLag)
            {
                _metrics?.ProjectionApplied(projection.Name, recorded.Event);
            }

            last = recorded.Position;
            applied++;

            if (applied % _checkpointEvery == 0)
            {
                progress?.Report(new RebuildProgress(projection.Name, applied, last));
            }
        }

        ReportEnd(progress, projection.Name, applied, last);
        return applied;
    }

    private void ReportEnd(IProgress<RebuildProgress>? progress, string projection, int applied, long last)
    {
        // a full batch was reported already, so don't send the same numbers twice
        if (applied == 0 || applied % _checkpointEvery != 0)
        {
            progress?.Report(new RebuildProgress(projection, applied, last));
        }
    }
}
