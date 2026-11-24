using LedgerCore.Application.EventStore;
using LedgerCore.Application.Queries;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

/// <summary>Every event of an account after AfterVersion, oldest first, read from the event store itself.</summary>
public sealed record GetAccountHistory(
    AccountId AccountId,
    long AfterVersion = 0,
    int PageSize = GetAccountHistory.DefaultPageSize) : IQuery<AccountHistoryPage?>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

/// <summary>One stored event: Amount is set for money movements, Detail for the IBAN or a freeze reason.</summary>
public sealed record AccountHistoryEntry(long Version, string Type, DateTimeOffset OccurredAt, Money? Amount, string? Detail);

/// <summary>One page of entries; <see cref="NextAfterVersion"/> is null on the last page.</summary>
public sealed record AccountHistoryPage(IReadOnlyList<AccountHistoryEntry> Entries, long? NextAfterVersion);

// an audit trail has to show what was stored, so this skips the read model and its lag
public sealed class GetAccountHistoryHandler(IEventStore events) : IQueryHandler<GetAccountHistory, AccountHistoryPage?>
{
    public async Task<AccountHistoryPage?> HandleAsync(GetAccountHistory query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.AfterVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.PageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, GetAccountHistory.MaxPageSize);

        var stream = AccountRepository.StreamFor(query.AccountId);
        var entries = await ReadAsync(stream, query.AfterVersion, query.PageSize + 1, cancellationToken);
        if (entries.Count == 0 && !await ExistsAsync(stream, cancellationToken))
        {
            return null;
        }

        if (entries.Count <= query.PageSize)
        {
            return new AccountHistoryPage(entries, null);
        }

        entries.RemoveAt(entries.Count - 1);
        return new AccountHistoryPage(entries, entries[^1].Version);
    }

    private async Task<List<AccountHistoryEntry>> ReadAsync(
        StreamId stream, long afterVersion, int limit, CancellationToken cancellationToken)
    {
        var entries = new List<AccountHistoryEntry>(limit);
        await foreach (var recorded in events.ReadStreamAsync(stream, afterVersion, cancellationToken))
        {
            entries.Add(ToEntry(recorded.Version, recorded.Event));
            if (entries.Count == limit)
            {
                break;
            }
        }

        return entries;
    }

    // a page past the end is empty for a real account too, so only the first event tells them apart
    private async Task<bool> ExistsAsync(StreamId stream, CancellationToken cancellationToken)
    {
        await foreach (var _ in events.ReadStreamAsync(stream, 0, cancellationToken))
        {
            return true;
        }

        return false;
    }

    private static AccountHistoryEntry ToEntry(long version, IDomainEvent domainEvent)
    {
        var type = domainEvent.GetType().Name;
        return domainEvent switch
        {
            AccountOpened opened => new(version, type, opened.OccurredAt, null, $"{opened.Iban} in {opened.Currency.Code}"),
            MoneyDeposited deposited => new(version, type, deposited.OccurredAt, deposited.Amount, null),
            MoneyWithdrawn withdrawn => new(version, type, withdrawn.OccurredAt, withdrawn.Amount, null),
            AccountFrozen frozen => new(version, type, frozen.OccurredAt, null, frozen.Reason.ToString()),
            _ => new(version, type, domainEvent.OccurredAt, null, null),
        };
    }
}
