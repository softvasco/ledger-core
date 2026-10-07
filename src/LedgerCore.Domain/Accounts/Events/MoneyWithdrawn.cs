using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts.Events;

public sealed record MoneyWithdrawn(AccountId AccountId, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;
