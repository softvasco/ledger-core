using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Tests.Accounts;

public class AccountSummaryProjectionTests
{
    private static readonly AccountId Id = AccountId.From(Guid.Parse("0199b0a4-0000-7000-8000-000000000001"));
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");
    private static readonly DateTimeOffset At = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly StreamId Stream = StreamId.For("account", Id.Value);

    [Fact]
    public void Opening_starts_an_open_account_with_a_zero_balance()
    {
        var summary = Fold(new AccountOpened(Id, SomeIban, Currency.Eur, At));

        Assert.Equal(new AccountSummary(Id, SomeIban, Currency.Eur, AccountStatus.Open, Money.Zero(Currency.Eur), 1), summary);
    }

    [Fact]
    public void Deposits_and_withdrawals_move_the_balance()
    {
        var summary = Fold(
            new AccountOpened(Id, SomeIban, Currency.Eur, At),
            new MoneyDeposited(Id, Money.Of(100m, Currency.Eur), At),
            new MoneyWithdrawn(Id, Money.Of(30.25m, Currency.Eur), At));

        Assert.Equal(Money.Of(69.75m, Currency.Eur), summary.Balance);
        Assert.Equal(3, summary.Version);
    }

    [Fact]
    public void Freezing_unfreezing_and_closing_change_the_status()
    {
        var opened = new AccountOpened(Id, SomeIban, Currency.Eur, At);

        Assert.Equal(AccountStatus.Frozen, Fold(opened, new AccountFrozen(Id, FreezeReason.CourtOrder, At)).Status);
        Assert.Equal(
            AccountStatus.Open,
            Fold(opened, new AccountFrozen(Id, FreezeReason.CourtOrder, At), new AccountUnfrozen(Id, At)).Status);
        Assert.Equal(AccountStatus.Closed, Fold(opened, new AccountClosed(Id, At)).Status);
    }

    [Fact]
    public void An_event_applied_twice_counts_once()
    {
        var opened = Recorded(1, new AccountOpened(Id, SomeIban, Currency.Eur, At));
        var deposited = Recorded(2, new MoneyDeposited(Id, Money.Of(10m, Currency.Eur), At));

        var once = AccountSummaryProjection.Apply(AccountSummaryProjection.Apply(null, opened), deposited);
        var twice = AccountSummaryProjection.Apply(once, deposited);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void An_event_before_the_account_was_opened_is_a_bug()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AccountSummaryProjection.Apply(null, Recorded(2, new MoneyDeposited(Id, Money.Of(10m, Currency.Eur), At))));

        Assert.Contains(nameof(MoneyDeposited), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Account_events_name_their_account_and_others_are_ignored()
    {
        Assert.True(AccountSummaryProjection.TryGetAccount(new AccountClosed(Id, At), out var id));
        Assert.Equal(Id, id);
        Assert.False(AccountSummaryProjection.TryGetAccount(new SomethingElse(At), out _));
    }

    private static AccountSummary Fold(params IDomainEvent[] events)
    {
        AccountSummary? summary = null;
        for (var i = 0; i < events.Length; i++)
        {
            summary = AccountSummaryProjection.Apply(summary, Recorded(i + 1, events[i]));
        }

        return summary!;
    }

    private static RecordedEvent Recorded(long version, IDomainEvent domainEvent) =>
        new(Stream, version, version, domainEvent);

    private sealed record SomethingElse(DateTimeOffset OccurredAt) : IDomainEvent;
}
