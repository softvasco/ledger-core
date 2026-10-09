using System.Collections.Concurrent;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Accounts;

namespace LedgerCore.Infrastructure.Accounts;

/// <summary>Account summaries in memory, filled from the store's global order. For tests.</summary>
public sealed class InMemoryAccountSummaries(IEventStore events) : IAccountSummaries, IDisposable
{
    private readonly ConcurrentDictionary<AccountId, AccountSummary> _summaries = new();
    private readonly SemaphoreSlim _catchingUp = new(1, 1);
    private long _checkpoint;

    /// <summary>The position of the last event applied.</summary>
    public long Checkpoint => Interlocked.Read(ref _checkpoint);

    public Task<AccountSummary?> GetAsync(AccountId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_summaries.TryGetValue(id, out var summary) ? summary : null);

    /// <summary>Applies the events stored after the checkpoint and returns how many.</summary>
    public async Task<int> CatchUpAsync(CancellationToken cancellationToken = default)
    {
        // one reader at a time, or two could apply the same events and race on the checkpoint
        await _catchingUp.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var applied = 0;
            await foreach (var recorded in events.ReadAllAsync(Checkpoint, cancellationToken).ConfigureAwait(false))
            {
                if (AccountSummaryProjection.TryGetAccount(recorded.Event, out var id))
                {
                    _summaries[id] = AccountSummaryProjection.Apply(_summaries.GetValueOrDefault(id), recorded);
                }

                Interlocked.Exchange(ref _checkpoint, recorded.Position);
                applied++;
            }

            return applied;
        }
        finally
        {
            _catchingUp.Release();
        }
    }

    public void Dispose() => _catchingUp.Dispose();
}
