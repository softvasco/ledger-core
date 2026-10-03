using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts.Events;

namespace LedgerCore.Infrastructure.EventStore.Serialization;

/// <summary>Stores events as JSON under a fixed name per event type.</summary>
public sealed class JsonEventSerializer : IEventSerializer
{
    // the stored name is part of the data, so renaming a class must never change it
    private static readonly FrozenDictionary<string, Type> TypesByName = new Dictionary<string, Type>
    {
        ["account_opened"] = typeof(AccountOpened),
        ["account_frozen"] = typeof(AccountFrozen),
        ["account_unfrozen"] = typeof(AccountUnfrozen),
        ["account_closed"] = typeof(AccountClosed),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<Type, string> NamesByType =
        TypesByName.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    public static IReadOnlyCollection<Type> KnownTypes => NamesByType.Keys;

    public SerializedEvent Serialize(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var type = domainEvent.GetType();
        if (!NamesByType.TryGetValue(type, out var name))
        {
            throw new InvalidOperationException($"{type.Name} has no stored name in {nameof(JsonEventSerializer)}.");
        }

        return new SerializedEvent(name, JsonSerializer.Serialize(domainEvent, TypeInfo(type)));
    }

    public IDomainEvent Deserialize(string eventType, string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventType);
        ArgumentException.ThrowIfNullOrEmpty(json);

        if (!TypesByName.TryGetValue(eventType, out var type))
        {
            throw new InvalidOperationException($"Unknown stored event type '{eventType}'.");
        }

        return (IDomainEvent)(JsonSerializer.Deserialize(json, TypeInfo(type))
            ?? throw new InvalidOperationException($"Stored '{eventType}' event is null."));
    }

    private static JsonTypeInfo TypeInfo(Type type) =>
        LedgerJsonContext.Default.GetTypeInfo(type)
        ?? throw new InvalidOperationException($"{type.Name} is missing from {nameof(LedgerJsonContext)}.");
}
