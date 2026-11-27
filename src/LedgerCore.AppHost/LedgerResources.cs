using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace LedgerCore.AppHost;

/// <summary>The API, its stores, the Service Bus emulator, Keycloak and the back office.</summary>
public static class LedgerResources
{
    public const string ProviderSetting = "Storage:Provider";
    public const string SqlServer = "SqlServer";
    public const string PostgreSql = "PostgreSql";
    public const string Realm = "ledger";
    public const string AdminClientId = "ledger-admin";

    public static IResourceBuilder<ProjectResource> AddLedger(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // SQL Server unless told otherwise, since the read model needs it anyway
        var provider = builder.Configuration[ProviderSetting] ?? SqlServer;
        var ledger = provider.Equals(SqlServer, StringComparison.OrdinalIgnoreCase)
            ? builder.AddSqlServer("sqlserver").AddDatabase("ledger").Resource
            : provider.Equals(PostgreSql, StringComparison.OrdinalIgnoreCase)
                ? (IResourceWithConnectionString)builder.AddPostgres("postgres").AddDatabase("ledger").Resource
                : throw new InvalidOperationException($"{ProviderSetting} is '{provider}'; use {SqlServer} or {PostgreSql}.");
        var database = builder.CreateResourceBuilder(ledger);

        var bus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();
        bus.AddServiceBusTopic("ledger-events", "ledger.events").AddServiceBusSubscription("audit");

        // the API reads ConnectionStrings:Ledger and ConnectionStrings:ServiceBus, as without Aspire
        return builder.AddProject<Projects.LedgerCore_Api>("api")
            .WithEnvironment("Storage__Provider", provider)
            .WithReference(database, "Ledger")
            .WaitFor(database)
            .WithReference(bus, "ServiceBus")
            .WaitFor(bus)
            .WithExternalHttpEndpoints();
    }

    public static IResourceBuilder<ProjectResource> AddBackOffice(
        this IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> api)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // local realm: a viewer, an operator and a user with no role; the admin only gets the issuer
        var keycloak = builder.AddKeycloak("keycloak")
            .WithRealmImport("./keycloak");

        return builder.AddProject<Projects.LedgerCore_Admin>("admin")
            .WithReference(api)
            .WaitFor(api)
            .WaitFor(keycloak)
            .WithEnvironment("Oidc__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/{Realm}"))
            .WithEnvironment("Oidc__ClientId", AdminClientId)
            .WithEnvironment("Oidc__RequireHttpsMetadata", "false")
            .WithExternalHttpEndpoints();
    }
}
