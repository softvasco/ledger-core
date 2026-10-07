using LedgerCore.Application.Accounts;
using LedgerCore.Application.Commands;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Infrastructure.Tests.Accounts;

// through the dispatcher, so the handlers are also proven to be found by AddCommands
public sealed class AccountCommandTests : IDisposable
{
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private readonly InMemoryEventStore _events = new();
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly ICommandDispatcher _dispatcher;

    public AccountCommandTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero)))
            .AddSingleton<IEventStore>(_events)
            .AddSingleton<ISnapshotStore<AccountSnapshot>>(new InMemorySnapshotStore<AccountSnapshot>())
            .AddSingleton(new SnapshotPolicy(100))
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddScoped<AccountRepository>()
            .AddCommands(typeof(OpenAccount).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _services.CreateScope();
        _dispatcher = _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_an_account_stores_it_and_returns_its_id()
    {
        var id = await Open();

        var stored = await _events.ReadStreamAsync(StreamId.For("account", id.Value), 0, Token).ToListAsync(Token);
        Assert.IsType<Domain.Accounts.Events.AccountOpened>(Assert.Single(stored).Event);
    }

    [Fact]
    public async Task Deposit_and_withdraw_answer_with_the_new_balance()
    {
        var id = await Open();

        var afterDeposit = await _dispatcher.DispatchAsync(new Deposit(id, Eur(100m)), Token);
        var afterWithdrawal = await _dispatcher.DispatchAsync(new Withdraw(id, Eur(40.5m)), Token);

        Assert.Equal(Eur(100m), afterDeposit.Value);
        Assert.Equal(Eur(59.5m), afterWithdrawal.Value);
    }

    [Fact]
    public async Task A_refused_withdrawal_stores_nothing()
    {
        var id = await Open();
        await _dispatcher.DispatchAsync(new Deposit(id, Eur(10m)), Token);

        var result = await _dispatcher.DispatchAsync(new Withdraw(id, Eur(10.01m)), Token);

        Assert.Equal(AccountErrors.InsufficientFundsCode, result.Error?.Code);
        Assert.Equal(2, await StoredEvents(id));
    }

    [Fact]
    public async Task Money_commands_on_an_unknown_account_are_refused()
    {
        var unknown = AccountId.From(Guid.CreateVersion7());

        var deposit = await _dispatcher.DispatchAsync(new Deposit(unknown, Eur(1m)), Token);
        var withdrawal = await _dispatcher.DispatchAsync(new Withdraw(unknown, Eur(1m)), Token);

        Assert.Equal(AccountErrors.NotFoundCode, deposit.Error?.Code);
        Assert.Equal(AccountErrors.NotFoundCode, withdrawal.Error?.Code);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private async Task<AccountId> Open() =>
        (await _dispatcher.DispatchAsync(new OpenAccount(SomeIban, Currency.Eur), Token)).Value;

    private async Task<int> StoredEvents(AccountId id) =>
        (await _events.ReadStreamAsync(StreamId.For("account", id.Value), 0, Token).ToListAsync(Token)).Count;
}
