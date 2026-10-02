using LedgerCore.Infrastructure.EventStore.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LedgerCore.Infrastructure.Tests.EventStore;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource? _dataSource;

    public PostgresEventStore Store { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        Store = new PostgresEventStore(_dataSource, new SomethingHappenedSerializer());
        await Store.CreateSchemaAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

public class PostgresEventStoreTests(PostgresFixture fixture)
    : EventStoreContract(fixture.Store), IClassFixture<PostgresFixture>;
