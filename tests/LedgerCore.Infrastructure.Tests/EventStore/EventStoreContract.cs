using LedgerCore.Application.EventStore;

namespace LedgerCore.Infrastructure.Tests.EventStore;

// every IEventStore has to pass these, so the PostgreSQL store gets the same tests later
public abstract class EventStoreContract
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly IEventStore _store;

    protected EventStoreContract(IEventStore store) => _store = store;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_append_creates_the_stream_with_one_version_per_event()
    {
        var stream = NewStream();

        var version = await _store.AppendAsync(stream, 0, Events(1, 2), Token);

        Assert.Equal(2, version);
        Assert.Equal([1L, 2L], (await ReadStream(stream)).Select(e => e.Version));
    }

    [Fact]
    public async Task A_stream_reads_back_in_the_order_it_was_written()
    {
        var stream = NewStream();
        await _store.AppendAsync(stream, 0, Events(1, 2), Token);
        await _store.AppendAsync(stream, 2, Events(3), Token);

        var events = await ReadStream(stream);

        Assert.Equal([1, 2, 3], events.Select(Number));
        Assert.All(events, e => Assert.Equal(stream, e.StreamId));
    }

    [Fact]
    public async Task Reading_after_a_version_skips_what_the_caller_already_has()
    {
        var stream = NewStream();
        await _store.AppendAsync(stream, 0, Events(1, 2, 3), Token);

        var events = await ReadStream(stream, afterVersion: 2);

        Assert.Equal(3, Assert.Single(events).Version);
    }

    [Fact]
    public async Task A_stream_that_was_never_written_is_empty()
    {
        Assert.Empty(await ReadStream(NewStream()));
    }

    [Fact]
    public async Task Appending_at_a_stale_version_is_a_conflict_and_writes_nothing()
    {
        var stream = NewStream();
        await _store.AppendAsync(stream, 0, Events(1, 2), Token);

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => _store.AppendAsync(stream, 1, Events(3), Token));

        Assert.Equal((1L, 2L), (conflict.ExpectedVersion, conflict.ActualVersion));
        Assert.Equal(2, (await ReadStream(stream)).Count);
    }

    [Fact]
    public async Task Creating_a_stream_that_already_exists_is_a_conflict()
    {
        var stream = NewStream();
        await _store.AppendAsync(stream, 0, Events(1), Token);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _store.AppendAsync(stream, 0, Events(2), Token));
    }

    [Fact]
    public async Task Only_one_of_several_racing_appends_wins()
    {
        var stream = NewStream();

        var attempts = Enumerable.Range(1, 10)
            .Select(n => Task.Run(() => _store.AppendAsync(stream, 0, Events(n), Token), Token));
        var outcome = await Task.WhenAll(attempts.Select(Succeeded));

        Assert.Single(outcome, won => won);
        Assert.Single(await ReadStream(stream));
    }

    [Fact]
    public async Task Read_all_returns_every_stream_in_the_order_it_was_stored()
    {
        var first = NewStream();
        var second = NewStream();
        var start = await LastPosition();
        await _store.AppendAsync(first, 0, Events(1), Token);
        await _store.AppendAsync(second, 0, Events(2), Token);
        await _store.AppendAsync(first, 1, Events(3), Token);

        var events = await ReadAll(start);

        Assert.Equal([1, 2, 3], events.Select(Number));
        Assert.Equal([first, second, first], events.Select(e => e.StreamId));
        Assert.True(events.Zip(events.Skip(1)).All(pair => pair.First.Position < pair.Second.Position));
    }

    [Fact]
    public async Task Read_all_continues_after_the_last_position_seen()
    {
        await _store.AppendAsync(NewStream(), 0, Events(1), Token);
        var checkpoint = await LastPosition();
        await _store.AppendAsync(NewStream(), 0, Events(2), Token);

        var events = await ReadAll(checkpoint);

        Assert.Equal(2, Number(Assert.Single(events)));
    }

    [Fact]
    public async Task An_empty_append_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AppendAsync(NewStream(), 0, [], Token));
    }

    private static StreamId NewStream() => StreamId.For("test", Guid.CreateVersion7());

    private static SomethingHappened[] Events(params int[] numbers) =>
        [.. numbers.Select(n => new SomethingHappened(n, Now))];

    private static int Number(RecordedEvent recorded) => ((SomethingHappened)recorded.Event).Number;

    private static async Task<bool> Succeeded(Task append)
    {
        try
        {
            await append;
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }

    private async Task<List<RecordedEvent>> ReadStream(StreamId stream, long afterVersion = 0) =>
        await _store.ReadStreamAsync(stream, afterVersion, Token).ToListAsync(Token);

    private async Task<List<RecordedEvent>> ReadAll(long afterPosition) =>
        await _store.ReadAllAsync(afterPosition, Token).ToListAsync(Token);

    private async Task<long> LastPosition() =>
        (await ReadAll(0)).Select(e => e.Position).DefaultIfEmpty(0).Max();
}
