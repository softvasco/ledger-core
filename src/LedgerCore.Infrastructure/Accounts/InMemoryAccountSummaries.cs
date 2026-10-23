using System.Collections.Concurrent;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.Infrastructure.Accounts;

/// <summary>Account summaries in memory, fed by the projection runner. For tests and local runs.</summary>
public sealed class InMemoryAccountSummaries : IAccountSummaries, IProjection
{
    private readonly ConcurrentDictionary<AccountId, AccountSummary> _summaries = new();

    public string Name => "account-summaries";

    public Task<AccountSummary?> GetAsync(AccountId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_summaries.TryGetValue(id, out var summary) ? summary : null);

    public Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        if (AccountSummaryProjection.TryGetAccount(recorded.Event, out var id))
        {
            _summaries[id] = AccountSummaryProjection.Apply(_summaries.GetValueOrDefault(id), recorded);
        }

        return Task.CompletedTask;
    }
}
