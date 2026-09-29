using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Ledger;

/// <summary>One line of a journal entry: an amount debited or credited to one account.</summary>
public sealed record Posting
{
    private Posting(AccountId accountId, PostingSide side, Money amount)
    {
        AccountId = accountId;
        Side = side;
        Amount = amount;
    }

    public AccountId AccountId { get; }

    public PostingSide Side { get; }

    /// <summary>Always positive. The side says which way the money moves.</summary>
    public Money Amount { get; }

    public static Posting Debit(AccountId accountId, Money amount) => Create(accountId, PostingSide.Debit, amount);

    public static Posting Credit(AccountId accountId, Money amount) => Create(accountId, PostingSide.Credit, amount);

    // a negative debit is just a credit in disguise, and two ways to write the same thing is how books stop matching
    private static Posting Create(AccountId accountId, PostingSide side, Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (accountId == default)
        {
            throw new ArgumentException("A posting needs an account.", nameof(accountId));
        }

        if (amount.IsNegative || amount.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A posting amount must be greater than zero.");
        }

        return new Posting(accountId, side, amount);
    }
}
