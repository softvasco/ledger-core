using LedgerCore.Application.Queries;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

/// <summary>Lines after AfterVersion, oldest first, that occurred in [From, To).</summary>
public sealed record GetStatement(
    AccountId AccountId,
    long AfterVersion = 0,
    int PageSize = GetStatement.DefaultPageSize,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null) : IQuery<StatementPage?>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

public sealed record StatementLine(
    long Version, StatementLineKind Kind, Money Amount, Money BalanceAfter, DateTimeOffset OccurredAt);

public enum StatementLineKind
{
    Deposit,
    Withdrawal,
}

/// <summary>One page of lines; <see cref="NextAfterVersion"/> is null on the last page.</summary>
public sealed record StatementPage(IReadOnlyList<StatementLine> Lines, long? NextAfterVersion);

/// <summary>Reads statement lines from wherever the projection keeps them.</summary>
public interface IAccountStatements
{
    /// <summary>Null when the read model hasn't seen the account.</summary>
    Task<StatementPage?> GetAsync(GetStatement query, CancellationToken cancellationToken = default);
}

public sealed class GetStatementHandler(IAccountStatements statements) : IQueryHandler<GetStatement, StatementPage?>
{
    public Task<StatementPage?> HandleAsync(GetStatement query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.AfterVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.PageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, GetStatement.MaxPageSize);
        return statements.GetAsync(query, cancellationToken);
    }
}

public static class StatementPaging
{
    /// <summary>Cuts a page from up to PageSize + 1 lines; the extra one says more exist.</summary>
    public static StatementPage Page(IReadOnlyList<StatementLine> fetched, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        if (fetched.Count <= pageSize)
        {
            return new StatementPage(fetched, null);
        }

        var lines = fetched.Take(pageSize).ToList();
        return new StatementPage(lines, lines[^1].Version);
    }
}
