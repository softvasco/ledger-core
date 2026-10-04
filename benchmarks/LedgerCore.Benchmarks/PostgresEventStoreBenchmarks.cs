using BenchmarkDotNet.Attributes;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.EventStore.Postgres;
using LedgerCore.Infrastructure.EventStore.Serialization;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LedgerCore.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public class PostgresEventStoreBenchmarks
{
    private const int LongStreamLength = 1_000;
    private const int BatchSize = 10;

    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private PostgreSqlContainer _container = null!;
    private NpgsqlDataSource _dataSource = null!;
    private PostgresEventStore _store = null!;
    private StreamId _longStream;

    [GlobalSetup]
    public async Task StartDatabase()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        _store = new PostgresEventStore(_dataSource, new JsonEventSerializer());
        await _store.CreateSchemaAsync();

        _longStream = NewStream();
        await _store.AppendAsync(_longStream, 0, History(_longStream, LongStreamLength));
    }

    [GlobalCleanup]
    public async Task StopDatabase()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Benchmark]
    public Task<long> AppendOneEvent()
    {
        var stream = NewStream();
        return _store.AppendAsync(stream, 0, History(stream, 1));
    }

    [Benchmark]
    public Task<long> AppendTenEventsInOneCall()
    {
        var stream = NewStream();
        return _store.AppendAsync(stream, 0, History(stream, BatchSize));
    }

    [Benchmark]
    public async Task<int> ReadStreamOf1000Events()
    {
        var count = 0;
        await foreach (var _ in _store.ReadStreamAsync(_longStream))
        {
            count++;
        }

        return count;
    }

    private static StreamId NewStream() => StreamId.For("account", Guid.CreateVersion7());

    // an account that keeps being frozen and released, so every event is a real one from the domain
    private static IDomainEvent[] History(StreamId stream, int length)
    {
        var id = AccountId.From(stream.Id);
        var at = DateTimeOffset.UnixEpoch;
        return
        [
            new AccountOpened(id, SomeIban, Currency.Eur, at),
            .. Enumerable.Range(1, length - 1).Select(i => i % 2 == 1
                ? (IDomainEvent)new AccountFrozen(id, FreezeReason.SuspectedFraud, at)
                : new AccountUnfrozen(id, at)),
        ];
    }
}
