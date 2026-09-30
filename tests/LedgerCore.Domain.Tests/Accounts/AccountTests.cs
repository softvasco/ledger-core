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
    public void Replaying_the_history_gives_the_same_state_with_nothing_pending()
    {
        var original = OpenAccount();
        original.Freeze(FreezeReason.SuspectedFraud, _clock);

        var replayed = Account.FromHistory(original.PendingEvents);

        Assert.Equal(original.Id, replayed.Id);
        Assert.Equal(original.Iban, replayed.Iban);
        Assert.Equal(AccountStatus.Frozen, replayed.Status);
        Assert.Equal(FreezeReason.SuspectedFraud, replayed.FreezeReason);
        Assert.Empty(replayed.PendingEvents);
        Assert.Equal(2, replayed.Version);
        Assert.Equal(2, replayed.CommittedVersion);
    }

    [Fact]
    public void History_must_start_with_the_account_being_opened()
    {
        var id = AccountId.New(_clock);

        Assert.Throws<ArgumentException>(() => Account.FromHistory([]));
        Assert.Throws<ArgumentException>(() => Account.FromHistory([new AccountClosed(id, Start)]));
    }

    private Account OpenAccount() => Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);

    private static void AssertRefused(Result result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(AccountErrors.InvalidStateCode, result.Error.Code);
    }
}
