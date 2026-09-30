using LedgerCore.Domain.Abstractions;

namespace LedgerCore.Domain.Accounts;

public static class AccountErrors
{
    public const string InvalidStateCode = "account.invalid_state";

    public static DomainError InvalidState(AccountId id, AccountStatus status, string action) =>
        new(InvalidStateCode, $"Account {id} is {status.ToString().ToLowerInvariant()} and can't be {action}.");
}
