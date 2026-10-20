namespace LedgerCore.Domain.Transfers;

public enum TransferStatus
{
    Requested,
    SourceDebited,
    Completed,
    Failed,
    Compensated,
}
