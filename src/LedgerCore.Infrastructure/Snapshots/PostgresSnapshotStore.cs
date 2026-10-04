using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.EventStore.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace LedgerCore.Infrastructure.Snapshots;

/// <summary>Latest snapshot per stream in the snapshots table, next to the events.</summary>
public sealed class PostgresSnapshotStore<TSnapshot> : ISnapshotStore<TSnapshot>
    where TSnapshot : class
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonTypeInfo<TSnapshot> _json;

    public PostgresSnapshotStore(NpgsqlDataSource dataSource, JsonTypeInfo<TSnapshot> json)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(json);

        _dataSource = dataSource;
        _json = json;
    }

    public async Task<TSnapshot?> LoadAsync(StreamId stream, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(
            "select data from snapshots where stream_category = @category and stream_id = @id");
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);

        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not string data)
        {
            return null;
        }

        // a snapshot from an older shape is just a cache miss, the events still rebuild the state
        try
        {
            return JsonSerializer.Deserialize(data, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        StreamId stream,
        long version,
        TSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var command = _dataSource.CreateCommand(
            """
            insert into snapshots (stream_category, stream_id, version, data)
            values (@category, @id, @version, @data)
            on conflict (stream_category, stream_id) do update
            set version = excluded.version, data = excluded.data, taken_at = now()
            where snapshots.version < excluded.version
            """);
        command.Parameters.AddWithValue("category", stream.Category);
        command.Parameters.AddWithValue("id", stream.Id);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(snapshot, _json));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Ready-made snapshot stores for the aggregates in this ledger.</summary>
public static class PostgresSnapshotStore
{
    public static PostgresSnapshotStore<AccountSnapshot> ForAccounts(NpgsqlDataSource dataSource) =>
        new(dataSource, LedgerJsonContext.Default.AccountSnapshot);
}
