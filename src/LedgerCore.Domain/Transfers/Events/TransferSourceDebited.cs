using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Transfers.Events;

public sealed record TransferSourceDebited(TransferId TransferId, DateTimeOffset OccurredAt) : IDomainEvent;
