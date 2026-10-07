using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Domain.Tests.Accounts;

public class AccountSnapshotTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private readonly FakeTimeProvider _clock = new(Start);

    [Fact]
    public void A_snapshot_plus_later_events_gives_the_same_account_as_a_full_replay()
    {
        var id = AccountId.New(_clock);
        var history = new List<IDomainEvent>
        {
            new AccountOpened(id, SomeIban, Currency.Eur, Start),
            new MoneyDeposited(id, Money.Of(100m, Currency.Eur), Start),
            new AccountFrozen(id, FreezeReason.SuspectedFraud, Start),
        };
        var snapshot = Account.FromHistory(history).ToSnapshot();
        var later = new IDomainEvent[]
        {
            new AccountUnfrozen(id, Start),
            new MoneyWithdrawn(id, Money.Of(30.25m, Currency.Eur), Start),
            new AccountFrozen(id, FreezeReason.CourtOrder, Start),
        };

        var fromSnapshot = Account.FromSnapshot(snapshot, later);
        var replayed = Account.FromHistory([.. history, .. later]);

        Assert.Equal(replayed.ToSnapshot(), fromSnapshot.ToSnapshot());
        Assert.Equal(Money.Of(69.75m, Currency.Eur), fromSnapshot.Balance);
        Assert.Equal(6, fromSnapshot.Version);
        Assert.Empty(fromSnapshot.PendingEvents);
    }

    [Fact]
    public void A_snapshot_keeps_the_state_and_the_version_it_was_taken_at()
    {
        var account = Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);
        account.Freeze(FreezeReason.Sanctions, _clock);
        account.MarkCommitted();

        var snapshot = account.ToSnapshot();

        Assert.Equal(
            new AccountSnapshot(
                account.Id, SomeIban, Currency.Eur, AccountStatus.Frozen, FreezeReason.Sanctions, Money.Zero(Currency.Eur), 2),
            snapshot);
    }

    [Fact]
    public void An_account_from_a_snapshot_can_still_change_and_tracks_its_version()
    {
        var account = Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);
        account.MarkCommitted();

        var restored = Account.FromSnapshot(account.ToSnapshot(), []);
        var result = restored.Close(_clock);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, restored.CommittedVersion);
        Assert.Equal(2, restored.Version);
    }

    [Fact]
    public void No_snapshot_while_changes_are_still_unsaved()
    {
        var account = Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);

        Assert.Throws<InvalidOperationException>(account.ToSnapshot);
    }
}
