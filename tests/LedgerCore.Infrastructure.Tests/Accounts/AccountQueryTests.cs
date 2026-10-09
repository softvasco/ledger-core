using LedgerCore.Application.Accounts;
using LedgerCore.Application.Commands;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Queries;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.Accounts;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Infrastructure.Tests.Accounts;

// commands write the stream, the projection catches up from the global order, the query reads it
public sealed class AccountQueryTests : IDisposable
{
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private readonly InMemoryEventStore _events = new();
    private readonly InMemoryAccountSummaries _summaries;
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    public AccountQueryTests()
    {
        _summaries = new InMemoryAccountSummaries(_events);
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero)))
            .AddSingleton<IEventStore>(_events)
            .AddSingleton<ISnapshotStore<AccountSnapshot>>(new InMemorySnapshotStore<AccountSnapshot>())
            .AddSingleton(new SnapshotPolicy(100))
            .AddSingleton<IAccountSummaries>(_summaries)
            .AddScoped<AccountRepository>()
            .AddCommands(typeof(OpenAccount).Assembly)
            .AddQueries(typeof(GetAccount).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _services.CreateScope();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ICommandDispatcher Commands => _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

    private IQueryDispatcher Queries => _scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

    [Fact]
    public async Task The_query_sees_the_account_once_the_projection_has_caught_up()
    {
        var id = (await Commands.DispatchAsync(new OpenAccount(SomeIban, Currency.Eur), Token)).Value;
        await Commands.DispatchAsync(new Deposit(id, Money.Of(100m, Currency.Eur)), Token);
        await Commands.DispatchAsync(new Withdraw(id, Money.Of(40.5m, Currency.Eur)), Token);

        Assert.Null(await Queries.DispatchAsync(new GetAccount(id), Token));

        Assert.Equal(3, await _summaries.CatchUpAsync(Token));
        var summary = await Queries.DispatchAsync(new GetAccount(id), Token);

        Assert.NotNull(summary);
        Assert.Equal(Money.Of(59.5m, Currency.Eur), summary.Balance);
        Assert.Equal(AccountStatus.Open, summary.Status);
        Assert.Equal(3, summary.Version);
    }

    [Fact]
    public async Task Catching_up_again_only_reads_what_was_stored_since()
    {
        var id = (await Commands.DispatchAsync(new OpenAccount(SomeIban, Currency.Eur), Token)).Value;
        await _summaries.CatchUpAsync(Token);

        await Commands.DispatchAsync(new Deposit(id, Money.Of(5m, Currency.Eur)), Token);

        Assert.Equal(1, await _summaries.CatchUpAsync(Token));
        Assert.Equal(0, await _summaries.CatchUpAsync(Token));
        Assert.Equal(Money.Of(5m, Currency.Eur), (await Queries.DispatchAsync(new GetAccount(id), Token))!.Balance);
    }

    [Fact]
    public async Task An_unknown_account_is_null_rather_than_an_error()
    {
        await _summaries.CatchUpAsync(Token);

        Assert.Null(await Queries.DispatchAsync(new GetAccount(AccountId.From(Guid.CreateVersion7())), Token));
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        _summaries.Dispose();
    }
}
