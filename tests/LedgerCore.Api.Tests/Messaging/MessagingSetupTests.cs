using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.ServiceBus;

namespace LedgerCore.Api.Tests.Messaging;

public sealed class ServiceBusEmulator : IAsyncLifetime
{
    private readonly ServiceBusContainer _container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
        .WithAcceptLicenseAgreement(true)
        .WithConfig(ComposeConfig())
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    // the file compose.yaml mounts, so the topology used locally is the one tested here
    private static string ComposeConfig([CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Join(Path.GetDirectoryName(testFile)!, "..", "..", "..", "deploy", "servicebus", "config.json"));
}

public class MessagingSetupTests(ServiceBusEmulator emulator) : IClassFixture<ServiceBusEmulator>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_connection_string_there_is_no_sender()
    {
        await using var api = new Api(connectionString: null);

        Assert.Null(api.Services.GetService<ServiceBusSender>());
    }

    [Fact]
    public async Task The_sender_publishes_to_the_events_topic_and_the_audit_subscription_gets_it()
    {
        await using var api = new Api(emulator.ConnectionString);
        var sender = api.Services.GetRequiredService<ServiceBusSender>();

        await sender.SendMessageAsync(new ServiceBusMessage("{}") { Subject = "ledger.account.opened.v1" }, Token);

        await using var client = new ServiceBusClient(emulator.ConnectionString);
        await using var receiver = client.CreateReceiver("ledger.events", "audit");
        var received = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10), Token);
        Assert.NotNull(received);
        Assert.Equal("ledger.account.opened.v1", received.Subject);
        await receiver.CompleteMessageAsync(received, Token);
    }

    private sealed class Api(string? connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (connectionString is not null)
            {
                builder.UseSetting("ConnectionStrings:ServiceBus", connectionString);
            }
        }
    }
}
