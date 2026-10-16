using LedgerCore.Application.Idempotency;

namespace LedgerCore.Infrastructure.Idempotency;

/// <summary>Idempotency keys in memory, one process only. For tests and local runs.</summary>
public sealed class InMemoryIdempotencyStore(IdempotencyOptions options, TimeProvider time) : IIdempotencyStore
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public Task<IdempotencyClaim> ClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry) || IsExpired(entry, now))
            {
                _entries[key] = new Entry(fingerprint, now, null);
                return Task.FromResult(IdempotencyClaim.Claimed);
            }

            return Task.FromResult(
                !entry.Fingerprint.AsSpan().SequenceEqual(fingerprint) ? IdempotencyClaim.Reused
                : entry.Response is null ? IdempotencyClaim.InProgress
                : IdempotencyClaim.Replay(entry.Response));
        }
    }

    public Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(response);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _entries[key] = entry with { Response = response };
            }
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.Response is null)
            {
                _entries.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private bool IsExpired(Entry entry, DateTimeOffset now) =>
        now - entry.ClaimedAt >= (entry.Response is null ? options.LockTimeout : options.Retention);

    private sealed record Entry(byte[] Fingerprint, DateTimeOffset ClaimedAt, StoredResponse? Response);
}
