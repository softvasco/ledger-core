using LedgerCore.Application.Queries;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Domain.Transfers;

namespace LedgerCore.Application.Transfers;

/// <summary>What a reader sees of a transfer. <see cref="Reason"/> is the refusing account's code, if any.</summary>
public sealed record TransferSummary(
    TransferId Id,
    AccountId Source,
    AccountId Destination,
    Money Amount,
    TransferStatus Status,
    string? Reason);

/// <summary>The transfer as its own stream has it, or null if there is no such transfer.</summary>
public sealed record GetTransfer(TransferId TransferId) : IQuery<TransferSummary?>;

// a transfer stream is a handful of events, so replaying it is cheaper than keeping a read model in step
public sealed class GetTransferHandler(TransferRepository transfers) : IQueryHandler<GetTransfer, TransferSummary?>
{
    public async Task<TransferSummary?> HandleAsync(GetTransfer query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await transfers.LoadAsync(query.TransferId, cancellationToken) is { } transfer
            ? new TransferSummary(transfer.Id, transfer.Source, transfer.Destination, transfer.Amount, transfer.Status, transfer.Reason)
            : null;
    }
}
