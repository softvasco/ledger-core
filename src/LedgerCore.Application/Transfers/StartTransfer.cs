using LedgerCore.Application.Commands;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Domain.Transfers;

namespace LedgerCore.Application.Transfers;

/// <summary>Records a transfer and runs it. A refusal on either side comes back with that account's code.</summary>
public sealed record StartTransfer(TransferId TransferId, AccountId Source, AccountId Destination, Money Amount)
    : ICommand<Result<TransferStatus>>;

public sealed class StartTransferHandler(TransferRepository transfers, TransferProcessManager manager, TimeProvider clock)
    : ICommandHandler<StartTransfer, Result<TransferStatus>>
{
    public async Task<Result<TransferStatus>> HandleAsync(StartTransfer command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var transfer = Transfer.Request(command.TransferId, command.Source, command.Destination, command.Amount, clock);
        await transfers.SaveAsync(transfer, cancellationToken);
        await manager.RunAsync(transfer, cancellationToken);

        // the account's code, so a refused transfer maps to the same problem as a refused withdrawal
        return transfer.Reason is { } code
            ? Result.Failure<TransferStatus>(new DomainError(code, $"Transfer {transfer.Id} was refused with {code}."))
            : Result.Success(transfer.Status);
    }
}
