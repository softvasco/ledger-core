using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Accounts.Events;

public sealed record AccountClosed(AccountId AccountId, DateTimeOffset OccurredAt) : IDomainEvent;
