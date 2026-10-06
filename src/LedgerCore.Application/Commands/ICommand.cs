namespace LedgerCore.Application.Commands;

/// <summary>A request to change something, answered by one handler with TResult.</summary>
#pragma warning disable CA1040 // the type parameter is the contract: it ties a command to its result
public interface ICommand<TResult>;
#pragma warning restore CA1040
