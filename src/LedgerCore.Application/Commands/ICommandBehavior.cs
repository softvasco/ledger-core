namespace LedgerCore.Application.Commands;

/// <summary>Runs around a command handler, for concerns every command shares (validation, logging, tracing).</summary>
public interface ICommandBehavior<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Calls <paramref name="continuation"/> to continue to the next behavior or the handler, or returns without calling it.</summary>
    Task<TResult> HandleAsync(TCommand command, Func<Task<TResult>> continuation, CancellationToken cancellationToken);
}
