namespace LedgerCore.Application.Queries;

/// <summary>A request to read something, answered by one handler with TResult. It never changes state.</summary>
#pragma warning disable CA1040 // the type parameter is the contract: it ties a query to its result
public interface IQuery<TResult>;
#pragma warning restore CA1040
