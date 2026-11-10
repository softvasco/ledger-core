using Azure.Messaging.ServiceBus;

namespace LedgerCore.Api.Messaging;

internal static class MessagingSetup
{
    public const string ConnectionStringName = "ServiceBus";
    public const string TopicSetting = "Messaging:Topic";
    public const string DefaultTopic = "ledger.events";

    /// <summary>A sender for the events topic when ConnectionStrings:ServiceBus is set.</summary>
    public static IServiceCollection AddLedgerMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        var topic = configuration[TopicSetting];
        if (string.IsNullOrWhiteSpace(topic))
        {
            topic = DefaultTopic;
        }

        // the client owns the AMQP connection, so one per process and every sender shares it
        services.AddSingleton(_ => new ServiceBusClient(connectionString));
        services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>().CreateSender(topic));
        return services;
    }
}
