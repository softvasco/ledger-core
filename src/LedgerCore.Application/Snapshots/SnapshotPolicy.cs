namespace LedgerCore.Application.Snapshots;

/// <summary>Takes a snapshot each time a stream passes another multiple of <see cref="Every"/>.</summary>
public sealed record SnapshotPolicy
{
    public SnapshotPolicy(int every)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(every);
        Every = every;
    }

    public static SnapshotPolicy Default { get; } = new(100);

    public int Every { get; }

    // crossing a multiple, not landing on it, since one append can add several events
    public bool IsDue(long versionBefore, long versionAfter) => versionAfter / Every > versionBefore / Every;
}
