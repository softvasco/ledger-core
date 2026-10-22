using LedgerCore.Infrastructure.EventStore.SqlServer;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace LedgerCore.Infrastructure.Tests.EventStore;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string Database = "ledger";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = null!;

    public SqlServerEventStore Store { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // the container only has master, and the tables shouldn't live there
        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"create database {Database}", master);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = Database }
            .ConnectionString;
        Store = new SqlServerEventStore(ConnectionString, new SomethingHappenedSerializer());
        await Store.CreateSchemaAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public class SqlServerEventStoreTests(SqlServerFixture fixture)
    : EventStoreContract(fixture.Store), IClassFixture<SqlServerFixture>
{
    [Fact]
    public async Task Creating_the_schema_twice_is_harmless()
    {
        await fixture.Store.CreateSchemaAsync(TestContext.Current.CancellationToken);
    }
}
