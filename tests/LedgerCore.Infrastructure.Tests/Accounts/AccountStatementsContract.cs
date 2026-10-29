using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Infrastructure.Tests.Accounts;

/// <summary>What every statement read model has to answer the same way.</summary>
public abstract class AccountStatementsContract
{
    private static readonly DateTimeOffset Day1 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AccountId _account = AccountId.From(Guid.CreateVersion7());
    private long _version;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected abstract Task<(IRebuildableProjection Projection, IAccountStatements Statements)> CreateAsync();

    [Fact]
    public async Task Lines_come_oldest_first_with_the_balance_after_each()
    {
        var (projection, statements) = await CreateAsync();
        await Feed(projection, Opened(), Deposited(100m, day: 1), Withdrawn(25.50m, day: 2));

        var page = await statements.GetAsync(new GetStatement(_account), Token);

        Assert.NotNull(page);
        Assert.Equal(
            [
                new StatementLine(2, StatementLineKind.Deposit, Eur(100m), Eur(100m), Day(1)),
                new StatementLine(3, StatementLineKind.Withdrawal, Eur(-25.50m), Eur(74.50m), Day(2)),
            ],
            page.Lines);
        Assert.Null(page.NextAfterVersion);
    }

    [Fact]
    public async Task Pages_follow_on_from_each_other_with_no_gaps_or_repeats()
    {
        var (projection, statements) = await CreateAsync();
        await Feed(projection, Opened(), Deposited(1m, 1), Deposited(2m, 2), Deposited(3m, 3), Deposited(4m, 4), Deposited(5m, 5));

        var first = await statements.GetAsync(new GetStatement(_account, PageSize: 2), Token);
        var second = await statements.GetAsync(new GetStatement(_account, first!.NextAfterVersion!.Value, 2), Token);
        var last = await statements.GetAsync(new GetStatement(_account, second!.NextAfterVersion!.Value, 2), Token);

        Assert.Equal([1m, 2m], first.Lines.Select(l => l.Amount.Amount));
        Assert.Equal([3m, 4m], second.Lines.Select(l => l.Amount.Amount));
        Assert.Equal([5m], last!.Lines.Select(l => l.Amount.Amount));
        Assert.Null(last.NextAfterVersion);
    }

    [Fact]
    public async Task From_is_inclusive_and_to_is_exclusive()
    {
        var (projection, statements) = await CreateAsync();
        await Feed(projection, Opened(), Deposited(1m, 1), Deposited(2m, 2), Deposited(3m, 3));

        var page = await statements.GetAsync(new GetStatement(_account, From: Day(2), To: Day(3)), Token);

        Assert.Equal([2m], page!.Lines.Select(l => l.Amount.Amount));
    }

    [Fact]
    public async Task An_account_with_no_money_moved_has_an_empty_statement()
    {
        var (projection, statements) = await CreateAsync();
        await Feed(projection, Opened());

        var page = await statements.GetAsync(new GetStatement(_account), Token);

        Assert.Empty(page!.Lines);
        Assert.Null(page.NextAfterVersion);
    }

    [Fact]
    public async Task An_account_the_read_model_has_not_seen_has_no_statement()
    {
        var (_, statements) = await CreateAsync();

        Assert.Null(await statements.GetAsync(new GetStatement(_account), Token));
    }

    [Fact]
    public async Task After_a_reset_the_same_events_build_the_same_statement()
    {
        var (projection, statements) = await CreateAsync();
        IDomainEvent[] events = [Opened(), Deposited(100m, day: 1), Withdrawn(25.50m, day: 2)];
        await Feed(projection, events);
        var before = await statements.GetAsync(new GetStatement(_account), Token);

        await projection.ResetAsync(Token);
        Assert.Null(await statements.GetAsync(new GetStatement(_account), Token));

        _version = 0;
        await Feed(projection, events);
        Assert.Equal(before!.Lines, (await statements.GetAsync(new GetStatement(_account), Token))!.Lines);
    }

    private async Task Feed(IProjection projection, params IDomainEvent[] events)
    {
        foreach (var domainEvent in events)
        {
            _version++;
            var recorded = new RecordedEvent(StreamId.For("account", _account.Value), _version, _version, domainEvent);
            await projection.ApplyAsync(recorded, Token);
        }
    }

    private static DateTimeOffset Day(int day) => Day1.AddDays(day - 1);

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private AccountOpened Opened() =>
        new(_account, Iban.Parse("PT50000201231234567890154"), Currency.Eur, Day1);

    private MoneyDeposited Deposited(decimal amount, int day) => new(_account, Eur(amount), Day(day));

    private MoneyWithdrawn Withdrawn(decimal amount, int day) => new(_account, Eur(amount), Day(day));
}
