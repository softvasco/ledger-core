namespace LedgerCore.Application.Idempotency;

/// <summary>What happened when a request tried to claim its key.</summary>
public abstract record IdempotencyClaim
{
    private IdempotencyClaim()
    {
    }

    public static IdempotencyClaim Claimed { get; } = new ClaimedKey();

    public static IdempotencyClaim InProgress { get; } = new KeyInProgress();

    public static IdempotencyClaim Reused { get; } = new KeyReused();

    public static IdempotencyClaim Replay(StoredResponse response) => new Completed(response);

    /// <summary>The key is new (or expired) and this request owns it now.</summary>
    public sealed record ClaimedKey : IdempotencyClaim;

    /// <summary>Another request with the same key hasn't finished yet.</summary>
    public sealed record KeyInProgress : IdempotencyClaim;

    /// <summary>The key was used before for a different request.</summary>
    public sealed record KeyReused : IdempotencyClaim;

    public sealed record Completed(StoredResponse Response) : IdempotencyClaim;
}
