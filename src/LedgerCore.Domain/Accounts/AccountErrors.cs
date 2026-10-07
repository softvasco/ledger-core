using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts;

public static class AccountErrors
{
    public const string NotFoundCode = "account.not_found";

    public const string InvalidStateCode = "account.invalid_state";

    public const string CurrencyMismatchCode = "account.currency_mismatch";

    public const string InsufficientFundsCode = "account.insufficient_funds";

    public static DomainError NotFound(AccountId id) => new(NotFoundCode, $"Account {id} does not exist.");

    public static DomainError InvalidState(AccountId id, AccountStatus status, string action) =>
        new(InvalidStateCode, $"Account {id} is {status.ToString().ToLowerInvariant()} and can't be {action}.");

    public static DomainError CurrencyMismatch(AccountId id, Currency account, Currency amount) =>
        new(CurrencyMismatchCode, $"Account {id} is in {account}, not {amount}.");

    public static DomainError InsufficientFunds(AccountId id, Money balance, Money requested) =>
        new(InsufficientFundsCode, $"Account {id} has {balance}, less than the {requested} requested.");
}
