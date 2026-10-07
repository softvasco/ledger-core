using LedgerCore.Application.Commands;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

/// <summary>Debits the account if it holds enough. The result is the balance after the withdrawal.</summary>
public sealed record Withdraw(AccountId AccountId, Money Amount) : ICommand<Result<Money>>;

public sealed class WithdrawHandler(AccountRepository accounts, TimeProvider clock)
    : ICommandHandler<Withdraw, Result<Money>>
{
    public Task<Result<Money>> HandleAsync(Withdraw command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return accounts.ChangeAsync(command.AccountId, account => account.Withdraw(command.Amount, clock), cancellationToken);
    }
}
