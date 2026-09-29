using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Tests.Ledger;

public class PostingTests
{
    private static readonly AccountId SomeAccount = AccountId.New(TimeProvider.System);

    [Fact]
    public void Debits_and_credits_keep_their_side_and_a_positive_amount()
    {
        var amount = Money.Of(12.50m, Currency.Eur);

        var debit = Posting.Debit(SomeAccount, amount);
        var credit = Posting.Credit(SomeAccount, amount);

        Assert.Equal(PostingSide.Debit, debit.Side);
        Assert.Equal(PostingSide.Credit, credit.Side);
        Assert.Equal(amount, debit.Amount);
        Assert.Equal(amount, credit.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_amounts_that_are_not_positive(decimal amount)
    {
        var money = Money.Of(amount, Currency.Eur);

        Assert.Throws<ArgumentOutOfRangeException>(() => Posting.Debit(SomeAccount, money));
        Assert.Throws<ArgumentOutOfRangeException>(() => Posting.Credit(SomeAccount, money));
    }

    [Fact]
    public void Rejects_a_posting_without_an_account()
    {
        Assert.Throws<ArgumentException>(() => Posting.Debit(default, Money.Of(1m, Currency.Eur)));
    }
}
