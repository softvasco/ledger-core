using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts.Events;

// no holder name here: events live forever and personal data has to be erasable (ADR-0002)
public sealed record AccountOpened(AccountId AccountId, Iban Iban, Currency Currency, DateTimeOffset OccurredAt) : IDomainEvent;
