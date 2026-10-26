using LedgerCore.Application.EventStore;
using LedgerCore.Infrastructure.EventStore.Postgres;
using LedgerCore.Infrastructure.EventStore.Serialization;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LedgerCore.Benchmarks;

public class PostgresEventStoreBenchmarks : EventStoreBenchmarks
{
    private PostgreSqlContainer _container = null!;
    private NpgsqlDataSource _dataSource = null!;

    protected override async Task<IEventStore> StartStoreAsync()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        var store = new PostgresEventStore(_dataSource, new JsonEventSerializer());
        await store.CreateSchemaAsync();
        return store;
    }

    protected override async Task StopStoreAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
