using LedgerCore.Application.EventStore;
using LedgerCore.Application.Idempotency;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.EventStore.Postgres;
using LedgerCore.Infrastructure.EventStore.Serialization;
using LedgerCore.Infrastructure.Idempotency;
using LedgerCore.Infrastructure.Snapshots;
using Npgsql;

namespace LedgerCore.Api.Storage;

internal static class StorageSetup
{
    public const string ConnectionStringName = "Ledger";

    /// <summary>PostgreSQL when ConnectionStrings:Ledger is set, in memory otherwise.</summary>
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

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<PostgresEventStore>();
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
        services.AddSingleton<ISnapshotStore<AccountSnapshot>>(sp =>
            PostgresSnapshotStore.ForAccounts(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();

        // registered before the read model catch-up, which reads the events table on start
        services.AddHostedService<SchemaSetup>();
        return services;
    }

    private sealed class SchemaSetup(PostgresEventStore store) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => store.CreateSchemaAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
