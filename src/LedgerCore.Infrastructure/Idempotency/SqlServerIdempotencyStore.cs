using System.Data;
using LedgerCore.Application.Idempotency;
using Microsoft.Data.SqlClient;

namespace LedgerCore.Infrastructure.Idempotency;

/// <summary>Idempotency keys in dbo.idempotency_keys, shared by every API instance.</summary>
public sealed class SqlServerIdempotencyStore : IIdempotencyStore
{
    // a released key can vanish between the insert and the read, then the claim just runs again
    private const int ClaimAttempts = 3;

    // same range locks as the snapshot upsert, so two claims of a new key can't both insert
    private const string Claim =
        """
        set xact_abort on;
        begin transaction;

        declare @claimed int;

        update dbo.idempotency_keys with (updlock, serializable)
        set fingerprint = @fingerprint, claimed_at = @now,
            status_code = null, content_type = null, location = null, body = null
        where idempotency_key = @key
          and ((status_code is null and claimed_at <= @stale) or claimed_at <= @expired);
        set @claimed = @@rowcount;

        if @claimed = 0
        begin
            insert into dbo.idempotency_keys (idempotency_key, fingerprint, claimed_at)
            select @key, @fingerprint, @now
            where not exists (
                select 1 from dbo.idempotency_keys with (updlock, serializable)
                where idempotency_key = @key);
            set @claimed = @@rowcount;
        end

        commit transaction;
        select @claimed;
        """;

    private readonly string _connectionString;
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _time;

    public SqlServerIdempotencyStore(string connectionString, IdempotencyOptions options, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        _connectionString = connectionString;
        _options = options;
        _time = time;
    }

    public async Task<IdempotencyClaim> ClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(fingerprint);

        for (var attempt = 0; attempt < ClaimAttempts; attempt++)
        {
            if (await TryClaimAsync(key, fingerprint, cancellationToken).ConfigureAwait(false))
            {
                return IdempotencyClaim.Claimed;
            }

            if (await ReadClaimAsync(key, fingerprint, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                return existing;
            }
        }

        return IdempotencyClaim.InProgress;
    }

    public async Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(response);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            update dbo.idempotency_keys
            set status_code = @status, content_type = @contentType, location = @location, body = @body
            where idempotency_key = @key and status_code is null
            """,
            connection);
        AddKey(command.Parameters, key);
        command.Parameters.Add("@status", SqlDbType.Int).Value = response.StatusCode;
        command.Parameters.Add("@contentType", SqlDbType.NVarChar, 200).Value = (object?)response.ContentType ?? DBNull.Value;
        command.Parameters.Add("@location", SqlDbType.NVarChar, 2000).Value = (object?)response.Location ?? DBNull.Value;
        command.Parameters.Add("@body", SqlDbType.VarBinary, -1).Value = response.Body;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            "delete from dbo.idempotency_keys where idempotency_key = @key and status_code is null",
            connection);
        AddKey(command.Parameters, key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(Claim, connection);
        AddKey(command.Parameters, key);
        command.Parameters.Add("@fingerprint", SqlDbType.VarBinary, 64).Value = fingerprint;
        command.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        command.Parameters.Add("@stale", SqlDbType.DateTimeOffset).Value = now - _options.LockTimeout;
        command.Parameters.Add("@expired", SqlDbType.DateTimeOffset).Value = now - _options.Retention;

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is 1;
    }

    private async Task<IdempotencyClaim?> ReadClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            """
            select fingerprint, status_code, content_type, location, body
            from dbo.idempotency_keys where idempotency_key = @key
            """,
            connection);
        AddKey(command.Parameters, key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!reader.GetFieldValue<byte[]>(0).AsSpan().SequenceEqual(fingerprint))
        {
            return IdempotencyClaim.Reused;
        }

        if (await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false))
        {
            return IdempotencyClaim.InProgress;
        }

        return IdempotencyClaim.Replay(new StoredResponse(
            reader.GetInt32(1),
            await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(2),
            await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
            reader.GetFieldValue<byte[]>(4)));
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

    private static void AddKey(SqlParameterCollection parameters, string key) =>
        parameters.Add("@key", SqlDbType.NVarChar, 255).Value = key;
}
