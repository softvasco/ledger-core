namespace LedgerCore.Domain.Accounts;

/// <summary>The account is not in a state that allows the requested change.</summary>
public sealed class AccountStateException : InvalidOperationException
{
    public AccountStateException()
    {
    }

    public AccountStateException(string message)
        : base(message)
    {
    }

    public AccountStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AccountStateException(AccountId id, AccountStatus status, string action)
        : base($"Account {id} is {status.ToString().ToLowerInvariant()} and can't be {action}.")
    {
    }
}
