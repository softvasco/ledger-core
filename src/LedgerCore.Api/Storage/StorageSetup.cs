using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Idempotency;
using LedgerCore.Application.Projections;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.Accounts;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.EventStore.Postgres;
using LedgerCore.Infrastructure.EventStore.Serialization;
using LedgerCore.Infrastructure.EventStore.SqlServer;
using LedgerCore.Infrastructure.Idempotency;
using LedgerCore.Infrastructure.ReadModels;
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
            AddInMemorySummaries(services);
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
        AddInMemorySummaries(services);
    }

    private static void AddSqlServer(IServiceCollection services, string connectionString)
    {
        services.AddSingleton(sp => new SqlServerEventStore(connectionString, sp.GetRequiredService<IEventSerializer>()));
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<SqlServerEventStore>());
        services.AddSingleton<ISnapshotStore<AccountSnapshot>>(_ => SqlServerSnapshotStore.ForAccounts(connectionString));
        services.AddSingleton<IIdempotencyStore>(sp => new SqlServerIdempotencyStore(
            connectionString, sp.GetRequiredService<IdempotencyOptions>(), sp.GetRequiredService<TimeProvider>()));

        // balances survive a restart here, so the catch-up carries on instead of replaying every event
        services.AddSingleton(_ => new SqlServerAccountReadModel(connectionString));
        services.AddSingleton<IAccountSummaries>(sp => sp.GetRequiredService<SqlServerAccountReadModel>());
        services.AddSingleton<IProjection>(sp => sp.GetRequiredService<SqlServerAccountReadModel>());

        // one SchemaSetup only: AddHostedService drops a second registration of the same type
        services.AddHostedService(sp => new SchemaSetup(async cancellationToken =>
        {
            await sp.GetRequiredService<SqlServerEventStore>().CreateSchemaAsync(cancellationToken);
            await sp.GetRequiredService<SqlServerAccountReadModel>().CreateSchemaAsync(cancellationToken);
        }));
    }

    private static void AddInMemorySummaries(IServiceCollection services)
    {
        services.AddSingleton<InMemoryAccountSummaries>();
        services.AddSingleton<IAccountSummaries>(sp => sp.GetRequiredService<InMemoryAccountSummaries>());
        services.AddSingleton<IProjection>(sp => sp.GetRequiredService<InMemoryAccountSummaries>());
    }

    private sealed class SchemaSetup(Func<CancellationToken, Task> createSchema) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => createSchema(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
