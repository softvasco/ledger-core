using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Application.EventStore;

/// <summary>Append-only storage for aggregate event streams.</summary>
public interface IEventStore
{
    /// <summary>
    /// Appends events to a stream if it still has <paramref name="expectedVersion"/> events (0 for a new stream).
    /// Returns the stream's new version.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">Someone else wrote to the stream first.</exception>
    Task<long> AppendAsync(
        StreamId stream,
        long expectedVersion,
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>Events of one stream in order, starting after <paramref name="afterVersion"/>. Empty if the stream doesn't exist.</summary>
    IAsyncEnumerable<RecordedEvent> ReadStreamAsync(
        StreamId stream,
        long afterVersion = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Events of every stream in the order they were stored, starting after <paramref name="afterPosition"/>.
    /// Positions only ever grow but may have gaps, so readers keep the last one they saw, not a count.
    /// </summary>
    IAsyncEnumerable<RecordedEvent> ReadAllAsync(
        long afterPosition = 0,
        CancellationToken cancellationToken = default);
}
