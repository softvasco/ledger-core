using System.Data;
using System.Runtime.CompilerServices;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using Microsoft.Data.SqlClient;

namespace LedgerCore.Infrastructure.EventStore.SqlServer;

/// <summary>Event store on one SQL Server table; see <see cref="CreateSchemaAsync"/>.</summary>
public sealed class SqlServerEventStore : IEventStore
{
    internal const string AppendLockResource = "ledger-core/events/append";

    private const string StreamVersionKey = "events_stream_version_key";
    private const int UniqueConstraintViolation = 2627;

    // two parameters a row, well under the 2,100 SQL Server allows in one command
    private const int RowsPerInsert = 500;

    private readonly string _connectionString;
    private readonly IEventSerializer _serializer;

    public SqlServerEventStore(string connectionString, IEventSerializer serializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(serializer);

        _connectionString = connectionString;
        _serializer = serializer;
    }

    public async Task CreateSchemaAsync(CancellationToken cancellationToken = default)
    {
        var schema = await ReadSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(schema, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> AppendAsync(
        StreamId stream,
        long expectedVersion,
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        RequireStream(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0 || events.Any(e => e is null))
        {
            throw new ArgumentException("Append needs at least one event and no nulls.", nameof(events));
        }

        var serialized = events.Select(_serializer.Serialize).ToArray();

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // IDENTITY hands out positions at insert, not at commit, so appends go one at a time
        await TakeAppendLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

        var actualVersion = await CurrentVersionAsync(connection, transaction, stream, cancellationToken)
            .ConfigureAwait(false);
        if (actualVersion != expectedVersion)
        {
            throw new ConcurrencyConflictException(stream, expectedVersion, actualVersion);
        }

        var version = expectedVersion;
        try
        {
            foreach (var chunk in serialized.Chunk(RowsPerInsert))
            {
                await InsertAsync(connection, transaction, stream, version, chunk, cancellationToken)
                    .ConfigureAwait(false);
                version += chunk.Length;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException e)
            when (e.Number == UniqueConstraintViolation && e.Message.Contains(StreamVersionKey, StringComparison.Ordinal))
        {
            // only reachable when something wrote to the stream without taking the append lock
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var current = await CurrentVersionAsync(connection, null, stream, cancellationToken).ConfigureAwait(false);
            throw new ConcurrencyConflictException(stream, expectedVersion, current);
        }

        return version;
    }

    public IAsyncEnumerable<RecordedEvent> ReadStreamAsync(
        StreamId stream,
        long afterVersion = 0,
        CancellationToken cancellationToken = default)
    {
        RequireStream(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(afterVersion);

        return ReadAsync(
            """
            select stream_category, stream_id, version, position, event_type, data
            from dbo.events
            where stream_category = @category and stream_id = @id and version > @after
            order by version
            """,
            parameters =>
            {
                AddStream(parameters, stream);
                parameters.Add("@after", SqlDbType.BigInt).Value = afterVersion;
            },
            cancellationToken);
    }

    public IAsyncEnumerable<RecordedEvent> ReadAllAsync(
        long afterPosition = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterPosition);

        return ReadAsync(
            """
            select stream_category, stream_id, version, position, event_type, data
            from dbo.events
            where position > @after
            order by position
            """,
            parameters => parameters.Add("@after", SqlDbType.BigInt).Value = afterPosition,
            cancellationToken);
    }

    private static void RequireStream(StreamId stream)
    {
        if (stream == default)
        {
            throw new ArgumentException("A stream id is required.", nameof(stream));
        }
    }

    private static void AddStream(SqlParameterCollection parameters, StreamId stream)
    {
        parameters.Add("@category", SqlDbType.NVarChar, 100).Value = stream.Category;
        parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stream.Id;
    }

    private static async Task TakeAppendLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("sp_getapplock", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure,
        };
        command.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = AppendLockResource;
        command.Parameters.Add("@LockMode", SqlDbType.VarChar, 32).Value = "Exclusive";
        command.Parameters.Add("@LockOwner", SqlDbType.VarChar, 32).Value = "Transaction";
        command.Parameters.Add("@LockTimeout", SqlDbType.Int).Value = -1;
        var result = command.Parameters.Add("@Result", SqlDbType.Int);
        result.Direction = ParameterDirection.ReturnValue;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // 0 and 1 mean granted; anything negative is a timeout, deadlock or error
        if ((int)result.Value < 0)
        {
            throw new InvalidOperationException($"sp_getapplock refused the append lock ({result.Value}).");
        }
    }

    private static async Task<long> CurrentVersionAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        StreamId stream,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "select isnull(max(version), 0) from dbo.events where stream_category = @category and stream_id = @id",
            connection,
            transaction);
        AddStream(command.Parameters, stream);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async Task InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        StreamId stream,
        long versionBefore,
        SerializedEvent[] events,
        CancellationToken cancellationToken)
    {
        var rows = string.Join(", ", events.Select((_, i) => $"({versionBefore + i + 1}, @type{i}, @data{i})"));

        // only insert ... select ... order by promises identity values in that order
        await using var command = new SqlCommand(
            $"""
            insert into dbo.events (stream_category, stream_id, version, event_type, data)
            select @category, @id, version, event_type, data
            from (values {rows}) as batch (version, event_type, data)
            order by version
            """,
            connection,
            transaction);
        AddStream(command.Parameters, stream);
        for (var i = 0; i < events.Length; i++)
        {
            command.Parameters.Add($"@type{i}", SqlDbType.NVarChar, 200).Value = events[i].EventType;
            command.Parameters.Add($"@data{i}", SqlDbType.NVarChar, -1).Value = events[i].Json;
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadSchemaAsync(CancellationToken cancellationToken)
    {
        var resource = typeof(SqlServerEventStore).Assembly
            .GetManifestResourceStream(typeof(SqlServerEventStore), "schema.sql")
            ?? throw new InvalidOperationException("schema.sql is missing from the assembly.");
        using var reader = new StreamReader(resource);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async IAsyncEnumerable<RecordedEvent> ReadAsync(
        string sql,
        Action<SqlParameterCollection> addParameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new SqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                addParameters(command.Parameters);
                var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        yield return new RecordedEvent(
                            StreamId.For(reader.GetString(0), reader.GetGuid(1)),
                            reader.GetInt64(2),
                            reader.GetInt64(3),
                            _serializer.Deserialize(reader.GetString(4), reader.GetString(5)));
                    }
                }
            }
        }
    }
}
