using BenchmarkDotNet.Attributes;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Benchmarks;

// same benchmarks for every engine, so the tables in docs/performance.md line up
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 15)]
public abstract class EventStoreBenchmarks
{
    private const int LongStreamLength = 1_000;
    private const int BatchSize = 10;

    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private IEventStore _store = null!;
    private StreamId _longStream;

    [GlobalSetup]
    public async Task StartDatabase()
    {
        _store = await StartStoreAsync();

        _longStream = NewStream();
        await _store.AppendAsync(_longStream, 0, History(_longStream, LongStreamLength));
    }

    [GlobalCleanup]
    public Task StopDatabase() => StopStoreAsync();

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

    protected abstract Task<IEventStore> StartStoreAsync();

    protected abstract Task StopStoreAsync();

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
