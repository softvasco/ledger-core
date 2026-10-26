using LedgerCore.Application.EventStore;
using LedgerCore.Infrastructure.EventStore.Serialization;
using LedgerCore.Infrastructure.EventStore.SqlServer;
using Testcontainers.MsSql;

namespace LedgerCore.Benchmarks;

public class SqlServerEventStoreBenchmarks : EventStoreBenchmarks
{
    private MsSqlContainer _container = null!;

    protected override async Task<IEventStore> StartStoreAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        var store = new SqlServerEventStore(_container.GetConnectionString(), new JsonEventSerializer());
        await store.CreateSchemaAsync();
        return store;
    }

    protected override Task StopStoreAsync() => _container.DisposeAsync().AsTask();
}
