namespace LedgerCore.Contracts.Transfers.V1;

// the domain event only has the id; a consumer shouldn't have to call back to learn the amount
[IntegrationEvent("ledger.transfer.completed", 1)]
public sealed record TransferCompleted(
    Guid TransferId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt);
