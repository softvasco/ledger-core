using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Application.Commands;

/// <summary>Runs the one handler registered for the command's runtime type, inside its behaviors.</summary>
public sealed class CommandDispatcher(IServiceProvider services) : ICommandDispatcher
{
    // closed handler type per command type, built once, since callers only see ICommand<TResult>
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    private static readonly MethodInfo CreateInvokerMethod =
        typeof(CommandDispatcher).GetMethod(nameof(CreateInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;

    public Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoker = (Invoker<TResult>)Invokers.GetOrAdd(
            command.GetType(),
            static (commandType, resultType) =>
                CreateInvokerMethod.MakeGenericMethod(commandType, resultType).Invoke(null, null)!,
            typeof(TResult));
        return invoker.InvokeAsync(services, command, cancellationToken);
    }

    private static Invoker<TCommand, TResult> CreateInvoker<TCommand, TResult>()
        where TCommand : ICommand<TResult> => new();

    private abstract class Invoker<TResult>
    {
        public abstract Task<TResult> InvokeAsync(
            IServiceProvider services, ICommand<TResult> command, CancellationToken cancellationToken);
    }

    private sealed class Invoker<TCommand, TResult> : Invoker<TResult>
        where TCommand : ICommand<TResult>
    {
        public override Task<TResult> InvokeAsync(
            IServiceProvider services, ICommand<TResult> command, CancellationToken cancellationToken)
        {
            // a second handler would silently take over, since the container returns the last one
            var handlers = services.GetServices<ICommandHandler<TCommand, TResult>>().ToArray();
            var handler = handlers.Length switch
            {
                1 => handlers[0],
                0 => throw new InvalidOperationException($"No handler is registered for {typeof(TCommand).Name}."),
                _ => throw new InvalidOperationException(
                    $"{handlers.Length} handlers are registered for {typeof(TCommand).Name}, expected one."),
            };
            var typed = (TCommand)command;
            Func<Task<TResult>> pipeline = () => handler.HandleAsync(typed, cancellationToken);

            // wrap from the inside out, so the first behavior registered is the first to run
            foreach (var behavior in services.GetServices<ICommandBehavior<TCommand, TResult>>().Reverse())
            {
                var next = pipeline;
                pipeline = () => behavior.HandleAsync(typed, next, cancellationToken);
            }

            return pipeline();
        }
    }
}
