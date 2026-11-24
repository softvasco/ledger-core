using LedgerCore.Application.Queries;

namespace LedgerCore.Application.Accounts;

/// <summary>Accounts ordered by IBAN, one numbered page at a time.</summary>
public sealed record ListAccounts(int Page = 1, int PageSize = ListAccounts.DefaultPageSize) : IQuery<AccountListPage>
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>One page of accounts and how many the read model has in total.</summary>
public sealed record AccountListPage(IReadOnlyList<AccountSummary> Accounts, int Page, int PageSize, int Total);

/// <summary>Lists account summaries from wherever the projection keeps them.</summary>
public interface IAccountList
{
    Task<AccountListPage> ListAsync(ListAccounts query, CancellationToken cancellationToken = default);
}

public sealed class ListAccountsHandler(IAccountList accounts) : IQueryHandler<ListAccounts, AccountListPage>
{
    public Task<AccountListPage> HandleAsync(ListAccounts query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.PageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, ListAccounts.MaxPageSize);
        return accounts.ListAsync(query, cancellationToken);
    }
}
