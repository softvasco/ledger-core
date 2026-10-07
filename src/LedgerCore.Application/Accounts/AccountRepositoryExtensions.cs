using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

internal static class AccountRepositoryExtensions
{
    // load, apply one change, save only if the rule allowed it; the balance is what money commands answer with
    public static async Task<Result<Money>> ChangeAsync(
        this AccountRepository accounts,
        AccountId id,
        Func<Account, Result> change,
        CancellationToken cancellationToken)
    {
        var account = await accounts.LoadAsync(id, cancellationToken);
        if (account is null)
        {
            return Result.Failure<Money>(AccountErrors.NotFound(id));
        }

        var result = change(account);
        if (!result.IsSuccess)
        {
            return Result.Failure<Money>(result.Error);
        }

        await accounts.SaveAsync(account, cancellationToken);
        return Result.Success(account.Balance);
    }
}
