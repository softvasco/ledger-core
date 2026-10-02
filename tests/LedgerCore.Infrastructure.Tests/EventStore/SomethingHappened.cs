using System.Text.Json;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Infrastructure.EventStore;

namespace LedgerCore.Infrastructure.Tests.EventStore;

internal sealed record SomethingHappened(int Number, DateTimeOffset OccurredAt) : IDomainEvent;

internal sealed class SomethingHappenedSerializer : IEventSerializer
{
    private const string EventType = "something_happened";

    public SerializedEvent Serialize(IDomainEvent domainEvent) =>
        new(EventType, JsonSerializer.Serialize((SomethingHappened)domainEvent));

    public IDomainEvent Deserialize(string eventType, string json) =>
        eventType == EventType
            ? JsonSerializer.Deserialize<SomethingHappened>(json)!
            : throw new InvalidOperationException($"Unknown event type '{eventType}'.");
}
