using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Transfers.Events;

public sealed record TransferCompleted(TransferId TransferId, DateTimeOffset OccurredAt) : IDomainEvent;
