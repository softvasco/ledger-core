using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.EventStore.Serialization;
using Microsoft.Data.SqlClient;

namespace LedgerCore.Infrastructure.Snapshots;

/// <summary>Latest snapshot per stream in dbo.snapshots, next to the events.</summary>
public sealed class SqlServerSnapshotStore<TSnapshot> : ISnapshotStore<TSnapshot>
    where TSnapshot : class
{
    // not MERGE: it can race two inserts of a new key, while these range locks make the second wait
    private const string Upsert =
        """
        set xact_abort on;
        begin transaction;

        update dbo.snapshots with (updlock, serializable)
        set version = @version, data = @data, taken_at = sysdatetimeoffset()
        where stream_category = @category and stream_id = @id and version < @version;

        if @@rowcount = 0
            insert into dbo.snapshots (stream_category, stream_id, version, data)
            select @category, @id, @version, @data
            where not exists (
                select 1 from dbo.snapshots with (updlock, serializable)
                where stream_category = @category and stream_id = @id);

        commit transaction;
        """;

    private readonly string _connectionString;
    private readonly JsonTypeInfo<TSnapshot> _json;

    public SqlServerSnapshotStore(string connectionString, JsonTypeInfo<TSnapshot> json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(json);

        _connectionString = connectionString;
        _json = json;
    }

    public async Task<TSnapshot?> LoadAsync(StreamId stream, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            "select data from dbo.snapshots where stream_category = @category and stream_id = @id",
            connection);
        AddStream(command.Parameters, stream);

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

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(Upsert, connection);
        AddStream(command.Parameters, stream);
        command.Parameters.Add("@version", SqlDbType.BigInt).Value = version;
        command.Parameters.Add("@data", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(snapshot, _json);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddStream(SqlParameterCollection parameters, StreamId stream)
    {
        parameters.Add("@category", SqlDbType.NVarChar, 100).Value = stream.Category;
        parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stream.Id;
    }
}

/// <summary>Ready-made snapshot stores for the aggregates in this ledger.</summary>
public static class SqlServerSnapshotStore
{
    public static SqlServerSnapshotStore<AccountSnapshot> ForAccounts(string connectionString) =>
        new(connectionString, LedgerJsonContext.Default.AccountSnapshot);
}
