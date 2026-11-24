using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.EventStore;

namespace LedgerCore.Infrastructure.Tests.Accounts;

public sealed class AccountHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 21, 0, 0, TimeSpan.Zero);
    private static readonly Iban SomeIban = Iban.Parse("PT50000201231234567890154");

    private readonly InMemoryEventStore _events = new();
    private readonly GetAccountHistoryHandler _handler;

    public AccountHistoryTests() => _handler = new GetAccountHistoryHandler(_events);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_event_comes_back_oldest_first_with_what_it_carried()
    {
        var id = AccountId.From(Guid.CreateVersion7());
        await Store(id,
            new AccountOpened(id, SomeIban, Currency.Eur, Now),
            new MoneyDeposited(id, Money.Of(120.25m, Currency.Eur), Now.AddMinutes(1)),
            new MoneyWithdrawn(id, Money.Of(20m, Currency.Eur), Now.AddMinutes(2)),
            new AccountFrozen(id, FreezeReason.SuspectedFraud, Now.AddMinutes(3)),
            new AccountUnfrozen(id, Now.AddMinutes(4)));

        var page = await _handler.HandleAsync(new GetAccountHistory(id), Token);

        Assert.NotNull(page);
        Assert.Null(page.NextAfterVersion);
        Assert.Equal(
            [
                new AccountHistoryEntry(1, "AccountOpened", Now, null, "PT50 0002 0123 1234 5678 9015 4 in EUR"),
                new AccountHistoryEntry(2, "MoneyDeposited", Now.AddMinutes(1), Money.Of(120.25m, Currency.Eur), null),
                new AccountHistoryEntry(3, "MoneyWithdrawn", Now.AddMinutes(2), Money.Of(20m, Currency.Eur), null),
                new AccountHistoryEntry(4, "AccountFrozen", Now.AddMinutes(3), null, "SuspectedFraud"),
                new AccountHistoryEntry(5, "AccountUnfrozen", Now.AddMinutes(4), null, null),
            ],
            page.Entries);
    }

    [Fact]
    public async Task Pages_follow_on_from_the_last_version_seen()
    {
        var id = await OpenWithDeposits(4);

        var first = await _handler.HandleAsync(new GetAccountHistory(id, PageSize: 3), Token);
        var second = await _handler.HandleAsync(new GetAccountHistory(id, first!.NextAfterVersion!.Value, 3), Token);

        Assert.Equal([1L, 2L, 3L], first.Entries.Select(e => e.Version));
        Assert.Equal(3, first.NextAfterVersion);
        Assert.Equal([4L, 5L], second!.Entries.Select(e => e.Version));
        Assert.Null(second.NextAfterVersion);
    }

    [Fact]
    public async Task A_page_that_ends_exactly_at_the_last_event_says_there_is_no_more()
    {
        var id = await OpenWithDeposits(2);

        var page = await _handler.HandleAsync(new GetAccountHistory(id, PageSize: 3), Token);

        Assert.Equal(3, page!.Entries.Count);
        Assert.Null(page.NextAfterVersion);
    }

    [Fact]
    public async Task Past_the_end_of_a_real_account_is_an_empty_page_not_null()
    {
        var id = await OpenWithDeposits(1);

        var page = await _handler.HandleAsync(new GetAccountHistory(id, AfterVersion: 9), Token);

        Assert.NotNull(page);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public async Task An_unknown_account_is_null()
    {
        var unknown = AccountId.From(Guid.CreateVersion7());

        Assert.Null(await _handler.HandleAsync(new GetAccountHistory(unknown), Token));
        Assert.Null(await _handler.HandleAsync(new GetAccountHistory(unknown, AfterVersion: 3), Token));
    }

    [Fact]
    public async Task Page_size_over_the_maximum_is_refused()
    {
        var query = new GetAccountHistory(AccountId.From(Guid.CreateVersion7()), PageSize: GetAccountHistory.MaxPageSize + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _handler.HandleAsync(query, Token));
    }

    private async Task<AccountId> OpenWithDeposits(int deposits)
    {
        var id = AccountId.From(Guid.CreateVersion7());
        IDomainEvent[] events =
        [
            new AccountOpened(id, SomeIban, Currency.Eur, Now),
            .. Enumerable.Range(1, deposits).Select(n => new MoneyDeposited(id, Money.Of(n, Currency.Eur), Now.AddMinutes(n))),
        ];
        await Store(id, events);
        return id;
    }

    private Task<long> Store(AccountId id, params IDomainEvent[] events) =>
        _events.AppendAsync(StreamId.For("account", id.Value), 0, events, Token);
}
