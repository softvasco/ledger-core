namespace LedgerCore.Application.Queries;

/// <summary>Finds the one handler for a query and runs it.</summary>
public interface IQueryDispatcher
{
    /// <summary>Runs the handler for the query and returns its result.</summary>
    /// <exception cref="InvalidOperationException">No handler, or more than one, for the query.</exception>
    Task<TResult> DispatchAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
