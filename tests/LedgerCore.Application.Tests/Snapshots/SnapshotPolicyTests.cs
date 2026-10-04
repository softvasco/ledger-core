using LedgerCore.Application.Snapshots;

namespace LedgerCore.Application.Tests.Snapshots;

public class SnapshotPolicyTests
{
    [Theory]
    [InlineData(0, 3, false)]
    [InlineData(4, 5, true)]
    [InlineData(5, 6, false)]
    [InlineData(3, 7, true)]
    [InlineData(4, 12, true)]
    [InlineData(10, 14, false)]
    public void A_snapshot_is_due_when_an_append_passes_a_multiple(long before, long after, bool due)
    {
        Assert.Equal(due, new SnapshotPolicy(5).IsDue(before, after));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_interval_has_to_be_positive(int every)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotPolicy(every));
    }
}
