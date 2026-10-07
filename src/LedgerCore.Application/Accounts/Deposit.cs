using LedgerCore.Application.Commands;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

/// <summary>Credits the account. The result is the balance after the deposit.</summary>
public sealed record Deposit(AccountId AccountId, Money Amount) : ICommand<Result<Money>>;

public sealed class DepositHandler(AccountRepository accounts, TimeProvider clock)
    : ICommandHandler<Deposit, Result<Money>>
{
    public Task<Result<Money>> HandleAsync(Deposit command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return accounts.ChangeAsync(command.AccountId, account => account.Deposit(command.Amount, clock), cancellationToken);
    }
}
