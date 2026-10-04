using System.Runtime.CompilerServices;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace LedgerCore.Infrastructure.EventStore.Postgres;

/// <summary>Event store on a single PostgreSQL table, created by <see cref="CreateSchemaAsync"/>.</summary>
public sealed class PostgresEventStore : IEventStore
{
    // any constant works, it only has to be the same for every writer
    internal const long AppendLockKey = 0x4C65646765;

    private const string StreamVersionKey = "events_stream_version_key";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IEventSerializer _serializer;

    public PostgresEventStore(NpgsqlDataSource dataSource, IEventSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(serializer);

        _dataSource = dataSource;
        _serializer = serializer;
    }

    public async Task CreateSchemaAsync(CancellationToken cancellationToken = default)
    {
        var schema = await ReadSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(schema);
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

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // one append at a time, so ReadAll never sees position 8 commit before position 7
        await using (var lockCommand = new NpgsqlCommand("select pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", AppendLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var actualVersion = await CurrentVersionAsync(connection, transaction, stream, cancellationToken)
            .ConfigureAwait(false);
        if (actualVersion != expectedVersion)
        {
            throw new ConcurrencyConflictException(stream, expectedVersion, actualVersion);
        }

        await using var batch = new NpgsqlBatch(connection, transaction);
        var version = expectedVersion;
        foreach (var @event in serialized)
        {
            batch.BatchCommands.Add(InsertCommand(stream, ++version, @event));
        }

        try
        {
            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException e)
            when (e is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StreamVersionKey })
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

        var command = _dataSource.CreateCommand(
            """
            select stream_category, stream_id, version, position, event_type, data
            from events
            where stream_category = @category and stream_id = @id and version > @after
            order by version
            """);
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        command.Parameters.AddWithValue("after", afterVersion);
        return ReadAsync(command, cancellationToken);
    }

    public IAsyncEnumerable<RecordedEvent> ReadAllAsync(
        long afterPosition = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterPosition);

        var command = _dataSource.CreateCommand(
            """
            select stream_category, stream_id, version, position, event_type, data
            from events
            where position > @after
            order by position
            """);
        command.Parameters.AddWithValue("after", afterPosition);
        return ReadAsync(command, cancellationToken);
    }

    private static void RequireStream(StreamId stream)
    {
        if (stream == default)
        {
            throw new ArgumentException("A stream id is required.", nameof(stream));
        }
    }

    private static async Task<long> CurrentVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        StreamId stream,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select coalesce(max(version), 0) from events where stream_category = @category and stream_id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static NpgsqlBatchCommand InsertCommand(StreamId stream, long version, SerializedEvent @event)
    {
        var command = new NpgsqlBatchCommand(
            """
            insert into events (stream_category, stream_id, version, event_type, data)
            values (@category, @id, @version, @type, @data)
            """);
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("type", @event.EventType);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, @event.Json);
        return command;
    }

    private static async Task<string> ReadSchemaAsync(CancellationToken cancellationToken)
    {
        var resource = typeof(PostgresEventStore).Assembly
            .GetManifestResourceStream(typeof(PostgresEventStore), "schema.sql")
            ?? throw new InvalidOperationException("schema.sql is missing from the assembly.");
        using var reader = new StreamReader(resource);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<RecordedEvent> ReadAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
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
