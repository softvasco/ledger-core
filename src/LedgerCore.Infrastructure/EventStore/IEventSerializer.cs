using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Infrastructure.EventStore;

/// <summary>Turns events into a stored type name and JSON, and back.</summary>
public interface IEventSerializer
{
    SerializedEvent Serialize(IDomainEvent domainEvent);

    IDomainEvent Deserialize(string eventType, string json);
}

public sealed record SerializedEvent(string EventType, string Json);
