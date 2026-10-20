using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Transfers;

namespace LedgerCore.Application.Transfers;

/// <summary>Loads transfers from their events and saves new ones. Transfers are short, so no snapshots.</summary>
public sealed class TransferRepository(IEventStore events)
{
    private const string Category = "transfer";

    public async Task<Transfer?> LoadAsync(TransferId id, CancellationToken cancellationToken = default)
    {
        var history = await events.ReadStreamAsync(StreamFor(id), 0, cancellationToken)
            .Select(e => e.Event)
            .ToListAsync(cancellationToken);

        return history.Count == 0 ? null : Transfer.FromHistory(history);
    }

    /// <exception cref="ConcurrencyConflictException">The transfer changed since it was loaded.</exception>
    public async Task SaveAsync(Transfer transfer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.PendingEvents.Count == 0)
        {
            return;
        }

        await events.AppendAsync(StreamFor(transfer.Id), transfer.CommittedVersion, [.. transfer.PendingEvents], cancellationToken);
        transfer.MarkCommitted();
    }

    private static StreamId StreamFor(TransferId id) => StreamId.For(Category, id.Value);
}
