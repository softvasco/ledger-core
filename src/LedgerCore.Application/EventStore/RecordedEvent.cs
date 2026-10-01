using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Application.EventStore;

/// <summary>An event as stored: its place in its own stream (version, from 1) and in the whole store (position).</summary>
public sealed record RecordedEvent(StreamId StreamId, long Version, long Position, IDomainEvent Event);
