using LedgerCore.Application.Idempotency;
using Npgsql;

namespace LedgerCore.Infrastructure.Idempotency;

/// <summary>Idempotency keys in the idempotency_keys table, shared by every instance of the API.</summary>
public sealed class PostgresIdempotencyStore : IIdempotencyStore
{
    // a released key can vanish between the insert and the read, then the claim just runs again
    private const int ClaimAttempts = 3;

    private readonly NpgsqlDataSource _dataSource;
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _time;

    public PostgresIdempotencyStore(NpgsqlDataSource dataSource, IdempotencyOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        _dataSource = dataSource;
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

        await using var command = _dataSource.CreateCommand(
            """
            update idempotency_keys
            set status_code = @status, content_type = @contentType, location = @location, body = @body
            where key = @key and status_code is null
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("status", response.StatusCode);
        command.Parameters.AddWithValue("contentType", (object?)response.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("location", (object?)response.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("body", response.Body);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await using var command = _dataSource.CreateCommand(
            "delete from idempotency_keys where key = @key and status_code is null");
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // the upsert only overwrites a row that has expired, so two racing claims can't both win
    private async Task<bool> TryClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        await using var command = _dataSource.CreateCommand(
            """
            insert into idempotency_keys (key, fingerprint, claimed_at)
            values (@key, @fingerprint, @now)
            on conflict (key) do update
            set fingerprint = excluded.fingerprint, claimed_at = excluded.claimed_at,
                status_code = null, content_type = null, location = null, body = null
            where (idempotency_keys.status_code is null and idempotency_keys.claimed_at <= @stale)
               or idempotency_keys.claimed_at <= @expired
            returning true
            """);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("stale", now - _options.LockTimeout);
        command.Parameters.AddWithValue("expired", now - _options.Retention);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private async Task<IdempotencyClaim?> ReadClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            "select fingerprint, status_code, content_type, location, body from idempotency_keys where key = @key");
        command.Parameters.AddWithValue("key", key);

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
}
