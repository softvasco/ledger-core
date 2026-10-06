namespace LedgerCore.Application.Commands;

/// <summary>Finds the one handler for a command and runs it.</summary>
public interface ICommandDispatcher
{
    /// <summary>Runs the handler for the command and returns its result.</summary>
    /// <exception cref="InvalidOperationException">No handler for the command.</exception>
    Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}
