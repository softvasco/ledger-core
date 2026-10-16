namespace LedgerCore.Application.Idempotency;

public sealed record IdempotencyOptions
{
    /// <summary>How long a finished response is replayed for the same key.</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>After this a claim that never finished (the process died) can be taken over.</summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromMinutes(1);
}
