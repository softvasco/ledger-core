using LedgerCore.Application.EventStore;
using LedgerCore.Application.Idempotency;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.EventStore.Postgres;
using LedgerCore.Infrastructure.EventStore.Serialization;
using LedgerCore.Infrastructure.EventStore.SqlServer;
using LedgerCore.Infrastructure.Idempotency;
using LedgerCore.Infrastructure.Snapshots;
using Npgsql;

namespace LedgerCore.Api.Storage;

internal static class StorageSetup
{
    public const string ConnectionStringName = "Ledger";
    public const string ProviderSetting = "Storage:Provider";
    public const string PostgreSql = "PostgreSql";
    public const string SqlServer = "SqlServer";

    /// <summary>The database in Storage:Provider when ConnectionStrings:Ledger is set, in memory otherwise.</summary>
    public static IServiceCollection AddLedgerStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IEventStore, InMemoryEventStore>();
            services.AddSingleton<ISnapshotStore<AccountSnapshot>, InMemorySnapshotStore<AccountSnapshot>>();
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
            return services;
        }

        // both add their schema setup before the read model catch-up, which reads events on start
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        var provider = configuration[ProviderSetting] ?? PostgreSql;
        if (provider.Equals(PostgreSql, StringComparison.OrdinalIgnoreCase))
        {
            AddPostgreSql(services, connectionString);
        }
        else if (provider.Equals(SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            AddSqlServer(services, connectionString);
        }
        else
        {
            throw new InvalidOperationException($"{ProviderSetting} is '{provider}'; use {PostgreSql} or {SqlServer}.");
        }

        return services;
    }

    private static void AddPostgreSql(IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<PostgresEventStore>();
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
        services.AddSingleton<ISnapshotStore<AccountSnapshot>>(sp =>
            PostgresSnapshotStore.ForAccounts(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddHostedService(sp => new SchemaSetup(sp.GetRequiredService<PostgresEventStore>().CreateSchemaAsync));
    }

    private static void AddSqlServer(IServiceCollection services, string connectionString)
    {
        services.AddSingleton(sp => new SqlServerEventStore(connectionString, sp.GetRequiredService<IEventSerializer>()));
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<SqlServerEventStore>());
        services.AddSingleton<ISnapshotStore<AccountSnapshot>>(_ => SqlServerSnapshotStore.ForAccounts(connectionString));
        services.AddSingleton<IIdempotencyStore>(sp => new SqlServerIdempotencyStore(
            connectionString, sp.GetRequiredService<IdempotencyOptions>(), sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService(sp => new SchemaSetup(sp.GetRequiredService<SqlServerEventStore>().CreateSchemaAsync));
    }

    private sealed class SchemaSetup(Func<CancellationToken, Task> createSchema) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => createSchema(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
