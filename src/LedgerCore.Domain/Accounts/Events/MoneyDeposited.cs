using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts.Events;

public sealed record MoneyDeposited(AccountId AccountId, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;
