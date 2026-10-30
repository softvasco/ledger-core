using BenchmarkDotNet.Attributes;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.ReadModels;
using Testcontainers.MsSql;

namespace LedgerCore.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public class SqlServerReadModelBenchmarks
{
    private const int Deposits = 1_000;
    private const int LinesInWindow = 50;

    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");
    private static readonly DateTimeOffset FirstDeposit = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private MsSqlContainer _container = null!;
    private SqlServerAccountReadModel _readModel = null!;
    private AccountId _account;
    private GetStatement _firstPage = null!;
    private GetStatement _lateWindow = null!;

    [GlobalSetup]
    public async Task StartDatabase()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _readModel = new SqlServerAccountReadModel(_container.GetConnectionString());
        await _readModel.CreateSchemaAsync();

        _account = AccountId.From(Guid.CreateVersion7());
        var stream = StreamId.For("account", _account.Value);
        var version = 0L;
        foreach (var @event in History())
        {
            version++;
            await _readModel.ApplyAsync(new RecordedEvent(stream, version, version, @event));
        }

        // one deposit an hour, so this window holds the 50 lines just before the last 50
        var windowStart = FirstDeposit.AddHours(Deposits - (2 * LinesInWindow));
        _firstPage = new GetStatement(_account);
        _lateWindow = new GetStatement(_account, From: windowStart, To: windowStart.AddHours(LinesInWindow));
    }

    [GlobalCleanup]
    public Task StopDatabase() => _container.DisposeAsync().AsTask();

    [Benchmark]
    public Task<AccountSummary?> GetBalance() => _readModel.GetAsync(_account);

    [Benchmark]
    public Task<StatementPage?> FirstStatementPage() => _readModel.GetAsync(_firstPage);

    [Benchmark]
    public Task<StatementPage?> StatementPageForLateDateRange() => _readModel.GetAsync(_lateWindow);

    private IEnumerable<IDomainEvent> History()
    {
        yield return new AccountOpened(_account, SomeIban, Currency.Eur, FirstDeposit.AddHours(-1));
        for (var i = 0; i < Deposits; i++)
        {
            yield return new MoneyDeposited(_account, Money.Of(10m, Currency.Eur), FirstDeposit.AddHours(i));
        }
    }
}
