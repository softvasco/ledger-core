using LedgerCore.Application.Accounts;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Transfers;

namespace LedgerCore.Application.Transfers;

/// <summary>Drives a transfer from where it stopped: debit the source, credit the destination, undo on refusal.</summary>
public sealed class TransferProcessManager(AccountRepository accounts, TransferRepository transfers, TimeProvider clock)
{
    // each step is saved before the next one starts, so a restart picks up at the last recorded step
    public async Task RunAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        if (transfer.Status == TransferStatus.Requested)
        {
            var debit = await accounts.ChangeAsync(transfer.Source, a => a.Withdraw(transfer.Amount, clock), cancellationToken);
            if (debit.IsSuccess)
            {
                transfer.SourceDebited(clock);
            }
            else
            {
                transfer.Fail(debit.Error.Code, clock);
            }

            await transfers.SaveAsync(transfer, cancellationToken);
        }

        if (transfer.Status == TransferStatus.SourceDebited)
        {
            await CreditAsync(transfer, cancellationToken);
        }
    }

    private async Task CreditAsync(Transfer transfer, CancellationToken cancellationToken)
    {
        var credit = await accounts.ChangeAsync(transfer.Destination, a => a.Deposit(transfer.Amount, clock), cancellationToken);
        if (credit.IsSuccess)
        {
            transfer.Complete(clock);
            await transfers.SaveAsync(transfer, cancellationToken);
            return;
        }

        var refund = await accounts.ChangeAsync(transfer.Source, a => a.Deposit(transfer.Amount, clock), cancellationToken);
        if (!refund.IsSuccess)
        {
            // the money is out of the source and nowhere else; stopping here keeps the transfer open for a person
            throw new InvalidOperationException(
                $"Transfer {transfer.Id} could not be credited ({credit.Error.Code}) or refunded ({refund.Error.Code}).");
        }

        transfer.Compensate(credit.Error.Code, clock);
        await transfers.SaveAsync(transfer, cancellationToken);
    }
}
