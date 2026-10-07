using LedgerCore.Domain.Abstractions;
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

    [Fact]
    public void Freezing_records_the_reason_and_blocks_the_account()
    {
        var account = OpenAccount();
        _clock.Advance(TimeSpan.FromHours(1));

        account.Freeze(FreezeReason.SuspectedFraud, _clock);

        Assert.Equal(AccountStatus.Frozen, account.Status);
        Assert.Equal(FreezeReason.SuspectedFraud, account.FreezeReason);
        Assert.Equal(
            new AccountFrozen(account.Id, FreezeReason.SuspectedFraud, Start.AddHours(1)),
            account.PendingEvents[^1]);
    }

    [Fact]
    public void Unfreezing_reopens_the_account_and_clears_the_reason()
    {
        var account = OpenAccount();
        account.Freeze(FreezeReason.CourtOrder, _clock);

        account.Unfreeze(_clock);

        Assert.Equal(AccountStatus.Open, account.Status);
        Assert.Null(account.FreezeReason);
        Assert.IsType<AccountUnfrozen>(account.PendingEvents[^1]);
    }

    [Fact]
    public void An_account_can_not_be_frozen_twice()
    {
        var account = OpenAccount();
        account.Freeze(FreezeReason.Sanctions, _clock);

        AssertRefused(account.Freeze(FreezeReason.CourtOrder, _clock));
        Assert.Equal(2, account.PendingEvents.Count);
    }

    [Fact]
    public void Only_a_frozen_account_can_be_unfrozen()
    {
        var account = OpenAccount();

        AssertRefused(account.Unfreeze(_clock));
    }

    [Fact]
    public void Rejects_a_freeze_reason_outside_the_list()
    {
        var account = OpenAccount();

        Assert.Throws<ArgumentOutOfRangeException>(() => account.Freeze((FreezeReason)42, _clock));
    }

    [Fact]
    public void Closing_an_open_account_raises_account_closed()
    {
        var account = OpenAccount();

        var result = account.Close(_clock);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Closed, account.Status);
        Assert.Equal(new AccountClosed(account.Id, Start), account.PendingEvents[^1]);
    }

    [Fact]
    public void A_frozen_account_can_not_be_closed_until_it_is_unfrozen()
    {
        var account = OpenAccount();
        account.Freeze(FreezeReason.CourtOrder, _clock);

        AssertRefused(account.Close(_clock));

        account.Unfreeze(_clock);
        account.Close(_clock);
        Assert.Equal(AccountStatus.Closed, account.Status);
    }

    [Fact]
    public void Nothing_changes_a_closed_account()
    {
        var account = OpenAccount();
        account.Close(_clock);

        AssertRefused(account.Close(_clock));
        AssertRefused(account.Freeze(FreezeReason.Sanctions, _clock));
        AssertRefused(account.Unfreeze(_clock));
    }

    [Fact]
    public void A_new_account_holds_nothing()
    {
        Assert.Equal(Money.Zero(Currency.Eur), OpenAccount().Balance);
    }

    [Fact]
    public void Deposits_and_withdrawals_move_the_balance()
    {
        var account = OpenAccount();

        account.Deposit(Eur(100m), _clock);
        account.Withdraw(Eur(30.25m), _clock);

        Assert.Equal(Eur(69.75m), account.Balance);
        Assert.Equal(new MoneyDeposited(account.Id, Eur(100m), Start), account.PendingEvents[1]);
        Assert.Equal(new MoneyWithdrawn(account.Id, Eur(30.25m), Start), account.PendingEvents[2]);
    }

    [Fact]
    public void Withdrawing_the_whole_balance_leaves_zero()
    {
        var account = OpenAccount();
        account.Deposit(Eur(50m), _clock);

        var result = account.Withdraw(Eur(50m), _clock);

        Assert.True(result.IsSuccess);
        Assert.True(account.Balance.IsZero);
    }

    [Fact]
    public void Withdrawing_more_than_the_balance_is_refused_and_records_nothing()
    {
        var account = OpenAccount();
        account.Deposit(Eur(50m), _clock);

        var result = account.Withdraw(Eur(50.01m), _clock);

        Assert.Equal(AccountErrors.InsufficientFundsCode, result.Error?.Code);
        Assert.Equal(Eur(50m), account.Balance);
        Assert.Equal(2, account.PendingEvents.Count);
    }

    [Fact]
    public void A_frozen_account_takes_deposits_but_pays_nothing_out()
    {
        var account = OpenAccount();
        account.Deposit(Eur(10m), _clock);
        account.Freeze(FreezeReason.CourtOrder, _clock);

        Assert.True(account.Deposit(Eur(5m), _clock).IsSuccess);
        AssertRefused(account.Withdraw(Eur(1m), _clock));
        Assert.Equal(Eur(15m), account.Balance);
    }

    [Fact]
    public void A_closed_account_takes_no_deposits()
    {
        var account = OpenAccount();
        account.Close(_clock);

        AssertRefused(account.Deposit(Eur(5m), _clock));
    }

    [Fact]
    public void Money_in_another_currency_is_refused()
    {
        var account = OpenAccount();
        account.Deposit(Eur(10m), _clock);

        Assert.Equal(AccountErrors.CurrencyMismatchCode, account.Deposit(Money.Of(5m, Currency.Usd), _clock).Error?.Code);
        Assert.Equal(AccountErrors.CurrencyMismatchCode, account.Withdraw(Money.Of(5m, Currency.Usd), _clock).Error?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_amount_must_be_greater_than_zero(decimal amount)
    {
        var account = OpenAccount();

        Assert.Throws<ArgumentOutOfRangeException>(() => account.Deposit(Eur(amount), _clock));
        Assert.Throws<ArgumentOutOfRangeException>(() => account.Withdraw(Eur(amount), _clock));
    }

    [Fact]
    public void Replaying_the_history_gives_the_same_state_with_nothing_pending()
    {
        var original = OpenAccount();
        original.Deposit(Eur(20m), _clock);
        original.Freeze(FreezeReason.SuspectedFraud, _clock);

        var replayed = Account.FromHistory(original.PendingEvents);

        Assert.Equal(original.Id, replayed.Id);
        Assert.Equal(original.Iban, replayed.Iban);
        Assert.Equal(AccountStatus.Frozen, replayed.Status);
        Assert.Equal(FreezeReason.SuspectedFraud, replayed.FreezeReason);
        Assert.Equal(Eur(20m), replayed.Balance);
        Assert.Empty(replayed.PendingEvents);
        Assert.Equal(3, replayed.Version);
        Assert.Equal(3, replayed.CommittedVersion);
    }

    [Fact]
    public void History_must_start_with_the_account_being_opened()
    {
        var id = AccountId.New(_clock);

        Assert.Throws<ArgumentException>(() => Account.FromHistory([]));
        Assert.Throws<ArgumentException>(() => Account.FromHistory([new AccountClosed(id, Start)]));
    }

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private Account OpenAccount() => Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);

    private static void AssertRefused(Result result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(AccountErrors.InvalidStateCode, result.Error.Code);
    }
}
