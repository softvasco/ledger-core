using LedgerCore.Application.Queries;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.Application.Accounts;

/// <summary>The account's summary as the read model has it, or null if it hasn't seen the account.</summary>
public sealed record GetAccount(AccountId AccountId) : IQuery<AccountSummary?>;

public sealed class GetAccountHandler(IAccountSummaries summaries) : IQueryHandler<GetAccount, AccountSummary?>
{
    public Task<AccountSummary?> HandleAsync(GetAccount query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return summaries.GetAsync(query.AccountId, cancellationToken);
    }
}
