using LedgerCore.Application.Commands;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

public sealed record OpenAccount(Iban Iban, Currency Currency) : ICommand<Result<AccountId>>;

public sealed class OpenAccountHandler(AccountRepository accounts, TimeProvider clock)
    : ICommandHandler<OpenAccount, Result<AccountId>>
{
    public async Task<Result<AccountId>> HandleAsync(OpenAccount command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var account = Account.Open(AccountId.New(clock), command.Iban, command.Currency, clock);
        await accounts.SaveAsync(account, cancellationToken);
        return Result.Success(account.Id);
    }
}
