using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Queries;

/// <summary>Runs the one handler registered for the query's runtime type.</summary>
// no behaviors here: reads don't need validation or a span of their own, the HTTP span covers them
public sealed class QueryDispatcher(IServiceProvider services) : IQueryDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    private static readonly MethodInfo CreateInvokerMethod =
        typeof(QueryDispatcher).GetMethod(nameof(CreateInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;

    public Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoker = (Invoker<TResult>)Invokers.GetOrAdd(
            query.GetType(),
            static (queryType, resultType) =>
                CreateInvokerMethod.MakeGenericMethod(queryType, resultType).Invoke(null, null)!,
            typeof(TResult));
        return invoker.InvokeAsync(services, query, cancellationToken);
    }

    private static Invoker<TQuery, TResult> CreateInvoker<TQuery, TResult>()
        where TQuery : IQuery<TResult> => new();

    private abstract class Invoker<TResult>
    {
        public abstract Task<TResult> InvokeAsync(
            IServiceProvider services, IQuery<TResult> query, CancellationToken cancellationToken);
    }

    private sealed class Invoker<TQuery, TResult> : Invoker<TResult>
        where TQuery : IQuery<TResult>
    {
        public override Task<TResult> InvokeAsync(
            IServiceProvider services, IQuery<TResult> query, CancellationToken cancellationToken)
        {
            var handlers = services.GetServices<IQueryHandler<TQuery, TResult>>().ToArray();
            var handler = handlers.Length switch
            {
                1 => handlers[0],
                0 => throw new InvalidOperationException($"No handler is registered for {typeof(TQuery).Name}."),
                _ => throw new InvalidOperationException(
                    $"{handlers.Length} handlers are registered for {typeof(TQuery).Name}, expected one."),
            };
            return handler.HandleAsync((TQuery)query, cancellationToken);
        }
    }
}
