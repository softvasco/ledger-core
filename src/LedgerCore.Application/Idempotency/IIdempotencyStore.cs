namespace LedgerCore.Application.Idempotency;

/// <summary>Remembers the response for each Idempotency-Key so a retried request gets it back instead of running again.</summary>
public interface IIdempotencyStore
{
    /// <summary>Claims <paramref name="key"/> for a new request, or says why it can't be claimed.</summary>
    Task<IdempotencyClaim> ClaimAsync(string key, byte[] fingerprint, CancellationToken cancellationToken = default);

    Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken = default);

    /// <summary>Forgets a claimed key whose request failed, so the client can retry it.</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}
