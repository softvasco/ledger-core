using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

/// <summary>What a reader sees of an account, as of <see cref="Version"/> of its stream.</summary>
public sealed record AccountSummary(
    AccountId Id,
    Iban Iban,
    Currency Currency,
    AccountStatus Status,
    Money Balance,
    long Version);

/// <summary>Reads account summaries from wherever the projection keeps them.</summary>
public interface IAccountSummaries
{
    Task<AccountSummary?> GetAsync(AccountId id, CancellationToken cancellationToken = default);
}
