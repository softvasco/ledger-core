using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.Projections;
using LedgerCore.Infrastructure.Tests.EventStore;

namespace LedgerCore.Infrastructure.Tests.Projections;

public class ProjectionRunnerTests
{
    private readonly InMemoryEventStore _events = new();
    private readonly CountingCheckpointStore _checkpoints = new();
    private readonly Dictionary<string, Guid> _streams = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_projection_gets_every_stored_event_in_the_global_order()
    {
        await Append("a", 1, 2);
        await Append("b", 3);
        var numbers = new Numbers("numbers");
        using var runner = Runner(numbers);

        Assert.Equal(3, await runner.CatchUpAsync(Token));

        Assert.Equal([1, 2, 3], numbers.Seen);
        Assert.Equal(3, await _checkpoints.GetAsync("numbers", Token));
    }

    [Fact]
    public async Task After_a_restart_a_new_runner_starts_from_the_saved_checkpoint()
    {
        await Append("a", 1, 2);
        using (var first = Runner(new Numbers("numbers")))
        {
            await first.CatchUpAsync(Token);
        }

        await Append("a", 3);
        var restarted = new Numbers("numbers");
        using var second = Runner(restarted);

        Assert.Equal(1, await second.CatchUpAsync(Token));
        Assert.Equal([3], restarted.Seen);
    }

    [Fact]
    public async Task A_failing_event_keeps_the_checkpoint_on_the_last_one_applied_and_the_retry_starts_there()
    {
        await Append("a", 1, 2, 3);
        var numbers = new Numbers("numbers") { FailOn = 2 };
        using var runner = Runner(numbers);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.CatchUpAsync(Token));
        Assert.Equal(1, await _checkpoints.GetAsync("numbers", Token));

        numbers.FailOn = null;
        Assert.Equal(2, await runner.CatchUpAsync(Token));
        Assert.Equal([1, 2, 3], numbers.Seen);
    }

    [Fact]
    public async Task A_projection_added_later_starts_from_the_beginning_without_moving_the_others()
    {
        await Append("a", 1, 2);
        using (var before = Runner(new Numbers("old")))
        {
            await before.CatchUpAsync(Token);
        }

        var old = new Numbers("old");
        var added = new Numbers("new");
        using var runner = Runner(old, added);

        Assert.Equal(2, await runner.CatchUpAsync(Token));
        Assert.Empty(old.Seen);
        Assert.Equal([1, 2], added.Seen);
    }

    [Fact]
    public async Task The_checkpoint_is_written_once_per_batch_and_once_at_the_end()
    {
        await Append("a", 1, 2, 3, 4, 5);
        using var runner = Runner(checkpointEvery: 2, new Numbers("numbers"));

        await runner.CatchUpAsync(Token);

        Assert.Equal([2, 4, 5], _checkpoints.Saved);
    }

    [Fact]
    public async Task A_projection_that_keeps_its_own_checkpoint_starts_there_and_the_runner_saves_nothing_for_it()
    {
        await Append("a", 1, 2, 3);
        var numbers = new SelfCheckpointedNumbers("numbers", checkpoint: 1);
        using var runner = Runner(numbers);

        Assert.Equal(2, await runner.CatchUpAsync(Token));

        Assert.Equal([2, 3], numbers.Seen);
        Assert.Equal(3, numbers.Checkpoint);
        Assert.Empty(_checkpoints.Saved);
    }

    [Fact]
    public async Task A_rebuild_empties_the_projection_and_applies_every_event_again()
    {
        await Append("a", 1, 2);
        await Append("b", 3);
        var numbers = new Numbers("numbers");
        using var runner = Runner(numbers);
        await runner.CatchUpAsync(Token);

        Assert.Equal(3, await runner.RebuildAsync("numbers", cancellationToken: Token));

        Assert.Equal([1, 2, 3], numbers.Seen);
        Assert.Equal(1, numbers.Resets);
        Assert.Equal(3, await _checkpoints.GetAsync("numbers", Token));
    }

    [Fact]
    public async Task A_rebuild_reports_progress_once_per_batch_and_once_at_the_end()
    {
        await Append("a", 1, 2, 3, 4, 5);
        using var runner = Runner(checkpointEvery: 2, new Numbers("numbers"));
        var progress = new RecordedProgress();

        await runner.RebuildAsync("numbers", progress, Token);

        Assert.Equal(
            [new("numbers", 2, 2), new("numbers", 4, 4), new RebuildProgress("numbers", 5, 5)],
            progress.Reports);
    }

    [Fact]
    public async Task A_rebuild_of_an_empty_store_still_reports_once()
    {
        using var runner = Runner(new Numbers("numbers"));
        var progress = new RecordedProgress();

        Assert.Equal(0, await runner.RebuildAsync("numbers", progress, Token));

        Assert.Equal([new RebuildProgress("numbers", 0, 0)], progress.Reports);
    }

    [Fact]
    public async Task A_projection_that_keeps_its_own_checkpoint_is_rebuilt_from_the_first_event()
    {
        await Append("a", 1, 2, 3);
        var numbers = new SelfCheckpointedNumbers("numbers", checkpoint: 3);
        using var runner = Runner(numbers);

        Assert.Equal(3, await runner.RebuildAsync("numbers", cancellationToken: Token));

        Assert.Equal([1, 2, 3], numbers.Seen);
        Assert.Empty(_checkpoints.Saved);
    }

    [Fact]
    public async Task A_rebuild_leaves_the_other_projections_alone()
    {
        await Append("a", 1, 2);
        var rebuilt = new Numbers("rebuilt");
        var other = new Numbers("other");
        using var runner = Runner(rebuilt, other);
        await runner.CatchUpAsync(Token);

        await runner.RebuildAsync("rebuilt", cancellationToken: Token);

        Assert.Equal(0, other.Resets);
        Assert.Equal([1, 2], other.Seen);
    }

    [Fact]
    public async Task Rebuilding_an_unknown_projection_names_it_in_the_error()
    {
        using var runner = Runner(new Numbers("numbers"));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => runner.RebuildAsync("nope", cancellationToken: Token));

        Assert.Contains("'nope'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_projection_that_cannot_be_emptied_cannot_be_rebuilt()
    {
        await Append("a", 1);
        using var runner = Runner(new AppendOnly("append-only"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RebuildAsync("append-only", cancellationToken: Token));
    }

    [Fact]
    public void Two_projections_cannot_share_a_checkpoint_name() =>
        Assert.Throws<ArgumentException>(() => Runner(new Numbers("same"), new Numbers("same")));

    private ProjectionRunner Runner(params IProjection[] projections) => Runner(100, projections);

    private ProjectionRunner Runner(int checkpointEvery, params IProjection[] projections) =>
        new(_events, _checkpoints, projections, checkpointEvery);

    private async Task Append(string stream, params int[] numbers)
    {
        var id = StreamId.For("test", StreamGuid(stream));
        var version = await _events.ReadStreamAsync(id, cancellationToken: Token).CountAsync(Token);
        await _events.AppendAsync(
            id, version, [.. numbers.Select(n => new SomethingHappened(n, DateTimeOffset.UnixEpoch))], Token);
    }

    private Guid StreamGuid(string stream)
    {
        if (!_streams.TryGetValue(stream, out var id))
        {
            _streams[stream] = id = Guid.CreateVersion7();
        }

        return id;
    }

    private sealed class Numbers(string name) : IRebuildableProjection
    {
        public string Name => name;

        public List<int> Seen { get; } = [];

        public int? FailOn { get; set; }

        public int Resets { get; private set; }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Seen.Clear();
            Resets++;
            return Task.CompletedTask;
        }

        public Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default)
        {
            var number = ((SomethingHappened)recorded.Event).Number;
            if (number == FailOn)
            {
                throw new InvalidOperationException($"can't apply {number}");
            }

            Seen.Add(number);
            return Task.CompletedTask;
        }
    }

    // stands in for a database projection that writes its rows and checkpoint together
    private sealed class SelfCheckpointedNumbers(string name, long checkpoint) : ICheckpointedProjection, IRebuildableProjection
    {
        public string Name => name;

        public List<int> Seen { get; } = [];

        public long Checkpoint { get; private set; } = checkpoint;

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Seen.Clear();
            Checkpoint = 0;
            return Task.CompletedTask;
        }

        public Task<long> GetCheckpointAsync(CancellationToken cancellationToken = default) => Task.FromResult(Checkpoint);

        public Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default)
        {
            Seen.Add(((SomethingHappened)recorded.Event).Number);
            Checkpoint = recorded.Position;
            return Task.CompletedTask;
        }
    }

    private sealed class AppendOnly(string name) : IProjection
    {
        public string Name => name;

        public Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // Progress<T> posts to the sync context, so the order of reports would depend on the scheduler
    private sealed class RecordedProgress : IProgress<RebuildProgress>
    {
        public List<RebuildProgress> Reports { get; } = [];

        public void Report(RebuildProgress value) => Reports.Add(value);
    }

    private sealed class CountingCheckpointStore : ICheckpointStore
    {
        private readonly InMemoryCheckpointStore _inner = new();

        public List<long> Saved { get; } = [];

        public Task<long> GetAsync(string projection, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(projection, cancellationToken);

        public Task SaveAsync(string projection, long position, CancellationToken cancellationToken = default)
        {
            Saved.Add(position);
            return _inner.SaveAsync(projection, position, cancellationToken);
        }
    }
}
