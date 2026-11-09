namespace LedgerCore.Contracts.Transfers.V1;

[IntegrationEvent("ledger.transfer.failed", 1)]
public sealed record TransferFailed(Guid TransferId, string Reason, DateTimeOffset OccurredAt);
