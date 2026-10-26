using System.Data;
using LedgerCore.Application.EventStore;
using LedgerCore.Infrastructure.EventStore.SqlServer;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace LedgerCore.Infrastructure.Tests.EventStore;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string Database = "ledger";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = null!;

    public SqlServerEventStore Store { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // readers see the last committed row instead of waiting, like PostgreSQL, which is the harder case
        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand(
                $"create database {Database}; alter database {Database} set read_committed_snapshot on;",
                master);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = Database }
            .ConnectionString;
        Store = new SqlServerEventStore(ConnectionString, new SomethingHappenedSerializer());
        await Store.CreateSchemaAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public class SqlServerEventStoreTests(SqlServerFixture fixture)
    : EventStoreContract(fixture.Store), IClassFixture<SqlServerFixture>
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_the_schema_twice_is_harmless()
    {
        await fixture.Store.CreateSchemaAsync(Token);
    }

    // same case as the PostgreSQL test: position 7 still open while position 8 tries to commit
    [Fact]
    public async Task Read_all_never_skips_an_event_that_commits_late()
    {
        var checkpoint = await LastPosition();

        await using var slowAppend = await Open();
        await using var slowTransaction = (SqlTransaction)await slowAppend.BeginTransactionAsync(Token);
        await TakeAppendLock(slowAppend, slowTransaction);
        await InsertRaw(slowAppend, slowTransaction, StreamId.For("test", Guid.CreateVersion7()), number: 1);

        var laterAppend = fixture.Store.AppendAsync(
            StreamId.For("test", Guid.CreateVersion7()), 0, [new SomethingHappened(2, DateTimeOffset.UnixEpoch)], Token);
        await WaitUntilBlocked("APPLICATION");

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
        await using var rogue = await Open();
        await using var rogueTransaction = (SqlTransaction)await rogue.BeginTransactionAsync(Token);
        await InsertRaw(rogue, rogueTransaction, stream, number: 1);

        var append = fixture.Store.AppendAsync(stream, 0, [new SomethingHappened(2, DateTimeOffset.UnixEpoch)], Token);
        await WaitUntilBlocked("KEY");
        await rogueTransaction.CommitAsync(Token);

        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => append.WaitAsync(WaitLimit, Token));
        Assert.Equal((0L, 1L), (conflict.ExpectedVersion, conflict.ActualVersion));
    }

    // more events than one insert statement takes, so the append is split
    [Fact]
    public async Task A_large_append_keeps_versions_and_positions_in_the_order_given()
    {
        var stream = StreamId.For("test", Guid.CreateVersion7());
        var events = Enumerable.Range(1, 1_201).Select(n => new SomethingHappened(n, DateTimeOffset.UnixEpoch)).ToArray();

        var version = await fixture.Store.AppendAsync(stream, 0, events, Token);

        var stored = await fixture.Store.ReadStreamAsync(stream, cancellationToken: Token).ToListAsync(Token);
        Assert.Equal(1_201, version);
        Assert.Equal(Enumerable.Range(1, 1_201), stored.Select(e => ((SomethingHappened)e.Event).Number));
        Assert.Equal(Enumerable.Range(1, 1_201).Select(v => (long)v), stored.Select(e => e.Version));
        Assert.Equal(stored.Select(e => e.Position).Order(), stored.Select(e => e.Position));
    }

    private static async Task TakeAppendLock(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = new SqlCommand("sp_getapplock", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
        };
        command.Parameters.AddWithValue("@Resource", SqlServerEventStore.AppendLockResource);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", "Transaction");
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task InsertRaw(
        SqlConnection connection, SqlTransaction transaction, StreamId stream, int number)
    {
        var serialized = new SomethingHappenedSerializer().Serialize(new SomethingHappened(number, DateTimeOffset.UnixEpoch));
        await using var command = new SqlCommand(
            """
            insert into dbo.events (stream_category, stream_id, version, event_type, data)
            values (@category, @id, 1, @type, @data)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@category", stream.Category);
        command.Parameters.AddWithValue("@id", stream.Id);
        command.Parameters.AddWithValue("@type", serialized.EventType);
        command.Parameters.AddWithValue("@data", serialized.Json);
        await command.ExecuteNonQueryAsync(Token);
    }

    // polls the lock DMV instead of sleeping, so the test is as fast as the database
    private async Task WaitUntilBlocked(string resourceType)
    {
        await using var connection = await Open();
        await using var command = new SqlCommand(
            "select count(*) from sys.dm_tran_locks where request_status = 'WAIT' and resource_type = @type",
            connection);
        command.Parameters.AddWithValue("@type", resourceType);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(WaitLimit);

        while ((int)(await command.ExecuteScalarAsync(timeout.Token))! == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private async Task<SqlConnection> Open()
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Token);
        return connection;
    }

    private async Task<List<RecordedEvent>> ReadAllAfter(long position) =>
        await fixture.Store.ReadAllAsync(position, Token).ToListAsync(Token);

    private async Task<long> LastPosition() =>
        (await ReadAllAfter(0)).Select(e => e.Position).DefaultIfEmpty(0).Max();
}
