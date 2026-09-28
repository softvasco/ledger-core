using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Domain.Tests.Accounts;

public class AccountTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private readonly FakeTimeProvider _clock = new(Start);

    [Fact]
    public void Opening_an_account_raises_account_opened()
    {
        var id = AccountId.New(_clock);

        var account = Account.Open(id, SomeIban, Currency.Eur, _clock);

        var opened = Assert.IsType<AccountOpened>(Assert.Single(account.PendingEvents));
        Assert.Equal(new AccountOpened(id, SomeIban, Currency.Eur, Start), opened);
    }

    [Fact]
    public void A_new_account_is_open_in_its_currency()
    {
        var account = OpenAccount();

        Assert.Equal(AccountStatus.Open, account.Status);
        Assert.Equal(Currency.Eur, account.Currency);
        Assert.Equal(SomeIban, account.Iban);
    }

    private Account OpenAccount() => Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);
}
