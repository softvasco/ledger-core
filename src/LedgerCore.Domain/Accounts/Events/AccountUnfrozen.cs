using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Accounts.Events;

public sealed record AccountUnfrozen(AccountId AccountId, DateTimeOffset OccurredAt) : IDomainEvent;
