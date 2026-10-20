using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Transfers.Events;

public sealed record TransferRequested(TransferId TransferId, AccountId Source, AccountId Destination, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;
