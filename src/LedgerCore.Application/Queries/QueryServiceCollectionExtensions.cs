using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Queries;

public static class QueryServiceCollectionExtensions
{
    /// <summary>Registers the query dispatcher and every query handler in the assemblies.</summary>
    public static IServiceCollection AddQueries(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        // scoped like the command side, so read models can share the request's connection
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();
        var handlers =
            from assembly in assemblies
            from type in assembly.GetTypes()
            where type is { IsAbstract: false, IsGenericTypeDefinition: false }
            from contract in type.GetInterfaces()
            where contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
            select (type, contract);
        foreach (var (implementation, contract) in handlers)
        {
            services.AddScoped(contract, implementation);
        }

        return services;
    }
}
