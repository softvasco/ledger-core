namespace LedgerCore.Domain.Abstractions;

/// <summary>Something that happened to an aggregate. Events are facts, so they are never changed after they are raised.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
