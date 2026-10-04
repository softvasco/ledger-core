using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace LedgerCore.Application.Accounts;

/// <summary>Loads accounts from the latest snapshot plus later events, and saves new events.</summary>
public sealed partial class AccountRepository(
    IEventStore events,
    ISnapshotStore<AccountSnapshot> snapshots,
    SnapshotPolicy policy,
    ILogger<AccountRepository> logger)
{
    private const string Category = "account";

    public async Task<Account?> LoadAsync(AccountId id, CancellationToken cancellationToken = default)
    {
        var stream = StreamFor(id);
        var snapshot = await snapshots.LoadAsync(stream, cancellationToken);
        var later = await events.ReadStreamAsync(stream, snapshot?.Version ?? 0, cancellationToken)
            .Select(e => e.Event)
            .ToListAsync(cancellationToken);

        if (snapshot is not null)
        {
            return Account.FromSnapshot(snapshot, later);
        }

        return later.Count == 0 ? null : Account.FromHistory(later);
    }

    /// <exception cref="ConcurrencyConflictException">The account changed since it was loaded.</exception>
    public async Task SaveAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.PendingEvents.Count == 0)
        {
            return;
        }

        var stream = StreamFor(account.Id);
        var versionBefore = account.CommittedVersion;
        await events.AppendAsync(stream, versionBefore, [.. account.PendingEvents], cancellationToken);
        account.MarkCommitted();

        if (policy.IsDue(versionBefore, account.Version))
        {
            await TrySnapshotAsync(stream, account, cancellationToken);
        }
    }

    private static StreamId StreamFor(AccountId id) => StreamId.For(Category, id.Value);

    // the events are stored by now, so a failed snapshot must not look like a failed save
    private async Task TrySnapshotAsync(StreamId stream, Account account, CancellationToken cancellationToken)
    {
        try
        {
            await snapshots.SaveAsync(stream, account.Version, account.ToSnapshot(), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LogSnapshotFailed(logger, e, stream, account.Version);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Snapshot of {Stream} at version {Version} failed")]
    private static partial void LogSnapshotFailed(ILogger logger, Exception exception, StreamId stream, long version);
}
