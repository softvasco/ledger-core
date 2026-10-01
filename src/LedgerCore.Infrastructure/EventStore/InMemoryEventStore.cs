using System.Runtime.CompilerServices;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Infrastructure.EventStore;

/// <summary>Keeps every stream in memory. For tests and local runs; nothing survives a restart.</summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly Lock _gate = new();
    private readonly List<RecordedEvent> _all = [];
    private readonly Dictionary<StreamId, List<RecordedEvent>> _streams = [];

    public Task<long> AppendAsync(
        StreamId stream,
        long expectedVersion,
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken = default)
    {
        RequireStream(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0 || events.Any(e => e is null))
        {
            throw new ArgumentException("Append needs at least one event and no nulls.", nameof(events));
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var recorded = _streams.TryGetValue(stream, out var existing) ? existing : [];
            if (recorded.Count != expectedVersion)
            {
                throw new ConcurrencyConflictException(stream, expectedVersion, recorded.Count);
            }

            foreach (var @event in events)
            {
                var entry = new RecordedEvent(stream, recorded.Count + 1, _all.Count + 1, @event);
                recorded.Add(entry);
                _all.Add(entry);
            }

            _streams[stream] = recorded;
            return Task.FromResult((long)recorded.Count);
        }
    }

    public IAsyncEnumerable<RecordedEvent> ReadStreamAsync(
        StreamId stream,
        long afterVersion = 0,
        CancellationToken cancellationToken = default)
    {
        RequireStream(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(afterVersion);

        lock (_gate)
        {
            var events = _streams.TryGetValue(stream, out var recorded)
                ? recorded.Where(e => e.Version > afterVersion).ToArray()
                : [];
            return Stream(events, cancellationToken);
        }
    }

    public IAsyncEnumerable<RecordedEvent> ReadAllAsync(
        long afterPosition = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterPosition);

        lock (_gate)
        {
            return Stream(_all.Where(e => e.Position > afterPosition).ToArray(), cancellationToken);
        }
    }

    private static void RequireStream(StreamId stream)
    {
        if (stream == default)
        {
            throw new ArgumentException("A stream id is required.", nameof(stream));
        }
    }

    // copied under the lock first, so a reader never sees an append that is half done
    private static async IAsyncEnumerable<RecordedEvent> Stream(
        RecordedEvent[] events,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var @event in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return @event;
        }
    }
}
