using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Accounts.Events;

public sealed record AccountFrozen(AccountId AccountId, FreezeReason Reason, DateTimeOffset OccurredAt) : IDomainEvent;
