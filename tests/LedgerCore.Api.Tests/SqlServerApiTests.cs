using Testcontainers.MsSql;

namespace LedgerCore.Api.Tests;

public sealed class SqlServerContainer : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public sealed class SqlServerApiTests(SqlServerContainer database)
    : StoredApiContract(database.ConnectionString, "SqlServer"), IClassFixture<SqlServerContainer>;
