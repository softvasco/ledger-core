using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LedgerCore.AppHost.Tests;

// builds the model only: nothing here starts a container
public class LedgerResourcesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task By_default_the_event_store_is_a_sql_server_database()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);

        Assert.Single(builder.Resources.OfType<SqlServerDatabaseResource>(), r => r.Name == "ledger");
        Assert.Empty(builder.Resources.OfType<PostgresServerResource>());
        Assert.Equal("SqlServer", await Environment(builder, "Storage__Provider"));
    }

    [Fact]
    public async Task The_provider_setting_moves_the_event_store_to_postgresql()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(
            ["--Storage:Provider", "PostgreSql"], Token);

        Assert.Single(builder.Resources.OfType<PostgresDatabaseResource>(), r => r.Name == "ledger");
        Assert.Empty(builder.Resources.OfType<SqlServerServerResource>());
        Assert.Equal("PostgreSql", await Environment(builder, "Storage__Provider"));
    }

    [Fact]
    public async Task An_unknown_provider_stops_the_apphost_with_the_choices()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(["--Storage:Provider", "Oracle"], Token));

        Assert.Contains("use SqlServer or PostgreSql", Innermost(error).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ConnectionStrings__Ledger", "{ledger.connectionString}")]
    [InlineData("ConnectionStrings__ServiceBus", "{servicebus.connectionString}")]
    public async Task The_api_gets_the_connection_strings_it_reads_without_aspire(string variable, string expected)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);

        Assert.Equal(expected, await Environment(builder, variable));
    }

    [Fact]
    public async Task The_api_waits_for_the_database_and_the_emulator()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);

        var waits = Api(builder).Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name).ToArray();

        // waiting on a database also waits on its server, so sqlserver shows up too
        Assert.Contains("ledger", waits);
        Assert.Contains("servicebus", waits);
    }

    [Fact]
    public async Task The_emulator_has_the_topic_and_subscription_the_api_and_compose_use()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);

        var topic = Assert.Single(builder.Resources.OfType<AzureServiceBusTopicResource>());
        Assert.Equal("ledger.events", topic.TopicName);
        Assert.Equal("audit", Assert.Single(builder.Resources.OfType<AzureServiceBusSubscriptionResource>()).SubscriptionName);
    }

    [Fact]
    public async Task The_back_office_reaches_the_ledger_only_through_the_api()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);

        var admin = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == "admin");

        Assert.Contains("api", admin.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name));
        Assert.Null(await Environment(builder, "ConnectionStrings__Ledger", "admin"));
    }

    [Fact]
    public async Task The_back_office_signs_in_against_the_local_keycloak_realm()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);

        var admin = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == "admin");

        Assert.Contains("keycloak", admin.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name));
        Assert.Equal("{keycloak.bindings.http.url}/realms/ledger", await Environment(builder, "Oidc__Authority", "admin"));
        Assert.Equal("ledger-admin", await Environment(builder, "Oidc__ClientId", "admin"));
    }

    [Fact]
    public async Task The_realm_file_gives_the_admin_client_and_the_roles_the_pages_check()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LedgerCore_AppHost>(Token);
        var file = Path.Combine(builder.AppHostDirectory, "keycloak", "ledger-realm.json");

        using var realm = JsonDocument.Parse(await File.ReadAllTextAsync(file, Token));

        var root = realm.RootElement;
        Assert.Equal(LedgerResources.Realm, root.GetProperty("realm").GetString());
        var client = root.GetProperty("clients").EnumerateArray().Single();
        Assert.Equal(LedgerResources.AdminClientId, client.GetProperty("clientId").GetString());
        Assert.Contains("http://localhost:5175/signin-oidc", client.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString()));
        Assert.Contains(client.GetProperty("protocolMappers").EnumerateArray(), m => m.GetProperty("config").GetProperty("claim.name").GetString() == "roles");
        Assert.Equal(
            ["ledger-operator", "ledger-viewer"],
            root.GetProperty("roles").GetProperty("realm").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Order());
    }

    private static ProjectResource Api(IDistributedApplicationTestingBuilder builder) =>
        builder.Resources.OfType<ProjectResource>().Single(r => r.Name == "api");

    // publish mode turns references into expressions instead of resolving live endpoints
    private static async Task<string?> Environment(
        IDistributedApplicationTestingBuilder builder, string variable, string resource = "api")
    {
        var project = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == resource);
        var configuration = await ExecutionConfigurationBuilder.Create(project)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish), NullLogger.Instance, Token);
        return configuration.EnvironmentVariables.FirstOrDefault(v => v.Key == variable).Value;
    }

    private static Exception Innermost(Exception error) => error.InnerException is null ? error : Innermost(error.InnerException);
}
