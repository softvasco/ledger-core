using System.Reflection;
using LedgerCore.Application.Diagnostics;
using LedgerCore.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Commands;

public static class CommandServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, the tracing and validation behaviors and every handler and validator in the assemblies, all scoped.</summary>
    public static IServiceCollection AddCommands(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        // scoped, so handlers resolve from the caller's scope and not from the root provider
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        // outermost first, so the span also covers commands that fail validation
        services.AddScoped(typeof(ICommandBehavior<,>), typeof(TracingBehavior<,>));
        services.AddScoped(typeof(ICommandBehavior<,>), typeof(ValidationBehavior<,>));
        foreach (var (implementation, contract) in Implementations(assemblies, typeof(ICommandHandler<,>), typeof(ICommandValidator<>)))
        {
            services.AddScoped(contract, implementation);
        }

        return services;
    }

    private static IEnumerable<(Type Implementation, Type Contract)> Implementations(
        IEnumerable<Assembly> assemblies, params Type[] openContracts) =>
        from assembly in assemblies
        from type in assembly.GetTypes()
        where type is { IsAbstract: false, IsGenericTypeDefinition: false }
        from contract in type.GetInterfaces()
        where contract.IsGenericType && openContracts.Contains(contract.GetGenericTypeDefinition())
        select (type, contract);
}
