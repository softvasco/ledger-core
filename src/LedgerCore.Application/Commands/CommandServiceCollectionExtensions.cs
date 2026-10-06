using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Commands;

public static class CommandServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler in the assemblies, all scoped.</summary>
    public static IServiceCollection AddCommands(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        // scoped, so handlers resolve from the caller's scope and not from the root provider
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        foreach (var (handler, contract) in Handlers(assemblies))
        {
            services.AddScoped(contract, handler);
        }

        return services;
    }

    private static IEnumerable<(Type Handler, Type Contract)> Handlers(IEnumerable<Assembly> assemblies) =>
        from assembly in assemblies
        from type in assembly.GetTypes()
        where type is { IsAbstract: false, IsGenericTypeDefinition: false }
        from contract in type.GetInterfaces()
        where contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
        select (type, contract);
}
