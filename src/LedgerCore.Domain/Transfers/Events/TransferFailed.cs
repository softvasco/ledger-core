using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Transfers.Events;

public sealed record TransferFailed(TransferId TransferId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
