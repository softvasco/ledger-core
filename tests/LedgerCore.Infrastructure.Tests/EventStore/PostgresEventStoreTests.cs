using LedgerCore.Application.EventStore;
using LedgerCore.Infrastructure.EventStore.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LedgerCore.Infrastructure.Tests.EventStore;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public PostgresEventStore Store { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        Store = new PostgresEventStore(DataSource, new SomethingHappenedSerializer());
        await Store.CreateSchemaAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

public class PostgresEventStoreTests(PostgresFixture fixture)
    : EventStoreContract(fixture.Store), IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // the case from the blog post: an earlier position still uncommitted while a later append tries to go ahead
    [Fact]
    public async Task Read_all_never_skips_an_event_that_commits_late()
    {
        var store = fixture.Store;
        var checkpoint = await LastPosition();

        await using var slowAppend = await fixture.DataSource.OpenConnectionAsync(Token);
        await using var slowTransaction = await slowAppend.BeginTransactionAsync(Token);
        await TakeAppendLock(slowAppend, slowTransaction);
        await InsertRaw(slowAppend, slowTransaction, StreamId.For("test", Guid.CreateVersion7()), number: 1);

        var laterAppend = store.AppendAsync(
            StreamId.For("test", Guid.CreateVersion7()), 0, [new SomethingHappened(2, DateTimeOffset.UnixEpoch)], Token);
        await WaitUntilBlocked("advisory");

        Assert.False(laterAppend.IsCompleted);
        Assert.Empty(await ReadAllAfter(checkpoint));

        await slowTransaction.CommitAsync(Token);
        await laterAppend.WaitAsync(WaitLimit, Token);

        var numbers = (await ReadAllAfter(checkpoint)).Select(e => ((SomethingHappened)e.Event).Number);
        Assert.Equal([1, 2], numbers);
    }

    [Fact]
    public async Task A_writer_that_skips_the_lock_still_gets_a_conflict_not_a_database_error()
    {
        var stream = StreamId.For("test", Guid.CreateVersion7());
        await using var rogue = await fixture.DataSource.OpenConnectionAsync(Token);
        await using var rogueTransaction = await rogue.BeginTransactionAsync(Token);
        await InsertRaw(rogue, rogueTransaction, stream, number: 1);

        var append = fixture.Store.AppendAsync(stream, 0, [new SomethingHappened(2, DateTimeOffset.UnixEpoch)], Token);
        await WaitUntilBlocked("transactionid");
        await rogueTransaction.CommitAsync(Token);

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => append.WaitAsync(WaitLimit, Token));
        Assert.Equal((0L, 1L), (conflict.ExpectedVersion, conflict.ActualVersion));
    }

    private static async Task TakeAppendLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand("select pg_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", PostgresEventStore.AppendLockKey);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task InsertRaw(
        NpgsqlConnection connection, NpgsqlTransaction transaction, StreamId stream, int number)
    {
        var serialized = new SomethingHappenedSerializer().Serialize(new SomethingHappened(number, DateTimeOffset.UnixEpoch));
        await using var command = new NpgsqlCommand(
            """
            insert into events (stream_category, stream_id, version, event_type, data)
            values (@category, @id, 1, @type, @data::jsonb)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        command.Parameters.AddWithValue("type", serialized.EventType);
        command.Parameters.AddWithValue("data", serialized.Json);
        await command.ExecuteNonQueryAsync(Token);
    }

    // polls pg_locks instead of sleeping, so the test is as fast as the database
    private async Task WaitUntilBlocked(string lockType)
    {
        await using var command = fixture.DataSource.CreateCommand(
            "select count(*) from pg_locks where locktype = @lockType and not granted");
        command.Parameters.AddWithValue("lockType", lockType);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(WaitLimit);

        while ((long)(await command.ExecuteScalarAsync(timeout.Token))! == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private async Task<List<RecordedEvent>> ReadAllAfter(long position) =>
        await fixture.Store.ReadAllAsync(position, Token).ToListAsync(Token);

    private async Task<long> LastPosition() =>
        (await ReadAllAfter(0)).Select(e => e.Position).DefaultIfEmpty(0).Max();
}
