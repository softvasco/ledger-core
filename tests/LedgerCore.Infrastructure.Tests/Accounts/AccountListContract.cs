using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Infrastructure.Tests.Accounts;

/// <summary>What every account list read model has to answer the same way.</summary>
public abstract class AccountListContract
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    // opened out of order on purpose, so the list order can't come from insert order
    private static readonly string[] Ibans =
    [
        "NL91ABNA0417164300",
        "BE68539007547034",
        "PT50000201231234567890154",
        "GB82WEST12345698765432",
        "FR1420041010050500013M02606",
        "DE89370400440532013000",
    ];

    private long _position;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected abstract Task<(IRebuildableProjection Projection, IAccountList Accounts)> CreateAsync();

    [Fact]
    public async Task Accounts_come_ordered_by_iban_with_the_total()
    {
        var (projection, accounts) = await CreateAsync();
        await OpenAll(projection);

        var page = await accounts.ListAsync(new ListAccounts(), Token);

        Assert.Equal(Ibans.Order(StringComparer.Ordinal), page.Accounts.Select(a => a.Iban.Value));
        Assert.Equal(Ibans.Length, page.Total);
    }

    [Fact]
    public async Task Pages_cover_every_account_once()
    {
        var (projection, accounts) = await CreateAsync();
        await OpenAll(projection);

        var first = await accounts.ListAsync(new ListAccounts(Page: 1, PageSize: 4), Token);
        var second = await accounts.ListAsync(new ListAccounts(Page: 2, PageSize: 4), Token);

        Assert.Equal(4, first.Accounts.Count);
        Assert.Equal(2, second.Accounts.Count);
        Assert.Equal(
            Ibans.Order(StringComparer.Ordinal),
            first.Accounts.Concat(second.Accounts).Select(a => a.Iban.Value));
        Assert.All([first, second], p => Assert.Equal(Ibans.Length, p.Total));
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_still_has_the_total()
    {
        var (projection, accounts) = await CreateAsync();
        await OpenAll(projection);

        var page = await accounts.ListAsync(new ListAccounts(Page: 3, PageSize: 4), Token);

        Assert.Empty(page.Accounts);
        Assert.Equal(Ibans.Length, page.Total);
    }

    [Fact]
    public async Task Listed_accounts_carry_their_balance_and_status()
    {
        var (projection, accounts) = await CreateAsync();
        var id = AccountId.From(Guid.CreateVersion7());
        await Feed(projection, id,
            new AccountOpened(id, Iban.Parse(Ibans[0]), Currency.Eur, Now),
            new MoneyDeposited(id, Money.Of(120.25m, Currency.Eur), Now),
            new AccountFrozen(id, FreezeReason.CustomerRequest, Now));

        var listed = Assert.Single((await accounts.ListAsync(new ListAccounts(), Token)).Accounts);

        Assert.Equal(id, listed.Id);
        Assert.Equal(Money.Of(120.25m, Currency.Eur), listed.Balance);
        Assert.Equal(AccountStatus.Frozen, listed.Status);
    }

    [Fact]
    public async Task An_empty_read_model_lists_nothing()
    {
        var (_, accounts) = await CreateAsync();

        var page = await accounts.ListAsync(new ListAccounts(), Token);

        Assert.Empty(page.Accounts);
        Assert.Equal(0, page.Total);
    }

    private async Task OpenAll(IProjection projection)
    {
        foreach (var iban in Ibans)
        {
            var id = AccountId.From(Guid.CreateVersion7());
            await Feed(projection, id, new AccountOpened(id, Iban.Parse(iban), Currency.Eur, Now));
        }
    }

    private async Task Feed(IProjection projection, AccountId id, params IDomainEvent[] events)
    {
        var version = 0L;
        foreach (var domainEvent in events)
        {
            // positions keep growing across accounts, like the global order of the store
            _position++;
            version++;
            var recorded = new RecordedEvent(StreamId.For("account", id.Value), version, _position, domainEvent);
            await projection.ApplyAsync(recorded, Token);
        }
    }
}
