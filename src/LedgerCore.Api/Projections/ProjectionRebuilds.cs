using System.Collections.Concurrent;
using LedgerCore.Application.Projections;

namespace LedgerCore.Api.Projections;

/// <summary>Background rebuilds, one per projection at a time, and their last status.</summary>
internal sealed partial class ProjectionRebuilds(
    ProjectionRunner runner,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<ProjectionRebuilds> logger)
{
    private readonly ConcurrentDictionary<string, RebuildStatus> _statuses = new(StringComparer.Ordinal);
    private readonly Lock _starting = new();

    public IReadOnlyList<string> Projections => runner.ProjectionNames;

    public bool Knows(string projection) => runner.ProjectionNames.Contains(projection, StringComparer.Ordinal);

    public RebuildStatus? StatusOf(string projection) => _statuses.GetValueOrDefault(projection);

    /// <summary>Null when that projection is still being rebuilt.</summary>
    public RebuildStatus? Start(string projection)
    {
        RebuildStatus started;
        lock (_starting)
        {
            if (StatusOf(projection) is { State: RebuildStatus.Running })
            {
                return null;
            }

            started = new RebuildStatus(projection, RebuildStatus.Running, 0, 0, clock.GetUtcNow(), null, null);
            _statuses[projection] = started;
        }

        _ = Task.Run(() => RunAsync(started), CancellationToken.None);
        return started;
    }

    private async Task RunAsync(RebuildStatus started)
    {
        var name = started.Projection;
        var progress = new StatusProgress(p => _statuses[name] = Current(name) with { Applied = p.Applied, Position = p.Position });
        try
        {
            var applied = await runner.RebuildAsync(name, progress, lifetime.ApplicationStopping);
            _statuses[name] = Current(name) with
            {
                State = RebuildStatus.Completed, Applied = applied, FinishedAt = clock.GetUtcNow(),
            };
        }
        catch (Exception e)
        {
            // catch-up carries on from where the rebuild stopped, so the model fills up again
            LogRebuildFailed(logger, name, e);
            _statuses[name] = Current(name) with
            {
                State = RebuildStatus.Failed, FinishedAt = clock.GetUtcNow(), Error = e.Message,
            };
        }
    }

    private RebuildStatus Current(string projection) => _statuses[projection];

    [LoggerMessage(Level = LogLevel.Error, Message = "Rebuilding projection {Projection} failed")]
    private static partial void LogRebuildFailed(ILogger logger, string projection, Exception exception);

    // reports straight away, unlike Progress<T>, which would post to a sync context
    private sealed class StatusProgress(Action<RebuildProgress> report) : IProgress<RebuildProgress>
    {
        public void Report(RebuildProgress value) => report(value);
    }
}

/// <summary>Where a rebuild has got to. State is running, completed or failed.</summary>
internal sealed record RebuildStatus(
    string Projection,
    string State,
    int Applied,
    long Position,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error)
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
