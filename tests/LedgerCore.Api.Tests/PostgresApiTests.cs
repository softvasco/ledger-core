using Testcontainers.PostgreSql;

namespace LedgerCore.Api.Tests;

public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public sealed class PostgresApiTests(PostgresContainer database)
    : StoredApiContract(database.ConnectionString, "PostgreSql"), IClassFixture<PostgresContainer>;
