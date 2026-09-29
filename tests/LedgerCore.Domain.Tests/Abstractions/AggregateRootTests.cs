using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Tests.Abstractions;

public class AggregateRootTests
{
    [Fact]
    public void A_new_aggregate_has_no_version_and_nothing_pending()
    {
        var counter = new Counter();

        Assert.Equal(0, counter.Version);
        Assert.Equal(0, counter.CommittedVersion);
        Assert.Empty(counter.PendingEvents);
    }

    [Fact]
    public void Raising_applies_the_event_and_keeps_it_pending()
    {
        var counter = new Counter();

        counter.Add(3);
        counter.Add(4);

        Assert.Equal(7, counter.Total);
        Assert.Equal(2, counter.Version);
        Assert.Equal(0, counter.CommittedVersion);
        Assert.Equal([new Added(3), new Added(4)], counter.PendingEvents);
    }

    [Fact]
    public void Replayed_events_count_towards_the_version_but_are_not_pending()
    {
        var counter = Counter.FromHistory([new Added(1), new Added(2)]);

        counter.Add(10);

        Assert.Equal(13, counter.Total);
        Assert.Equal(3, counter.Version);
        Assert.Equal(2, counter.CommittedVersion);
        Assert.Equal([new Added(10)], counter.PendingEvents);
    }

    [Fact]
    public void Marking_committed_moves_the_committed_version_up()
    {
        var counter = new Counter();
        counter.Add(5);

        counter.MarkCommitted();

        Assert.Empty(counter.PendingEvents);
        Assert.Equal(1, counter.CommittedVersion);
        Assert.Equal(1, counter.Version);
    }

    [Fact]
    public void An_event_that_fails_to_apply_is_not_recorded()
    {
        var counter = new Counter();

        Assert.Throws<ArgumentOutOfRangeException>(() => counter.Add(-1));

        Assert.Equal(0, counter.Version);
        Assert.Empty(counter.PendingEvents);
    }

    private sealed record Added(int Amount) : IDomainEvent
    {
        public DateTimeOffset OccurredAt => DateTimeOffset.UnixEpoch;
    }

    private sealed class Counter : AggregateRoot<Guid>
    {
        public int Total { get; private set; }

        public static Counter FromHistory(IEnumerable<IDomainEvent> history)
        {
            var counter = new Counter();
            counter.Replay(history);
            return counter;
        }

        public void Add(int amount) => Raise(new Added(amount));

        protected override void Apply(IDomainEvent domainEvent)
        {
            var added = (Added)domainEvent;
            ArgumentOutOfRangeException.ThrowIfNegative(added.Amount);
            Total += added.Amount;
        }
    }
}
