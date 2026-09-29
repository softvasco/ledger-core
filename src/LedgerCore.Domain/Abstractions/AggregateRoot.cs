namespace LedgerCore.Domain.Abstractions;

/// <summary>
/// Base for event-sourced aggregates. State only changes in <see cref="Apply"/>, both when a new event is raised
/// and when history is replayed, so the two paths can't drift apart.
/// </summary>
public abstract class AggregateRoot<TId>
    where TId : struct
{
    private readonly List<IDomainEvent> _pendingEvents = [];

    public TId Id { get; protected set; }

    /// <summary>How many events the aggregate has, stored or pending.</summary>
    public long Version { get; private set; }

    /// <summary>The version the store already has. Appends are checked against this, so a concurrent writer loses.</summary>
    public long CommittedVersion => Version - _pendingEvents.Count;

    public IReadOnlyList<IDomainEvent> PendingEvents => _pendingEvents;

    /// <summary>Call once the pending events are safely stored.</summary>
    public void MarkCommitted() => _pendingEvents.Clear();

    protected abstract void Apply(IDomainEvent domainEvent);

    protected void Raise(IDomainEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        Apply(@event);
        Version++;
        _pendingEvents.Add(@event);
    }

    protected void Replay(IEnumerable<IDomainEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        foreach (var @event in history)
        {
            Apply(@event);
            Version++;
        }
    }
}
