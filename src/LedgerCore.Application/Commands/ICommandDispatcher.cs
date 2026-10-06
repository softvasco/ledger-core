namespace LedgerCore.Application.Commands;

/// <summary>Finds the one handler for a command and runs it.</summary>
public interface ICommandDispatcher
{
    /// <exception cref="InvalidOperationException">No handler for the command.</exception>
    Task<TResult> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
}
