using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Transfers.Events;

public sealed record TransferCompensated(TransferId TransferId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
