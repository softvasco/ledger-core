using System.Collections.Concurrent;
using System.Collections.Immutable;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Projections;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;

namespace LedgerCore.Infrastructure.Accounts;

/// <summary>Summaries and statements in memory, for tests and local runs.</summary>
public sealed class InMemoryAccountSummaries : IAccountSummaries, IAccountStatements, IRebuildableProjection
{
    private readonly ConcurrentDictionary<AccountId, AccountSummary> _summaries = new();
    private readonly ConcurrentDictionary<AccountId, ImmutableList<StatementLine>> _lines = new();

    public string Name => "account-summaries";

    public Task<AccountSummary?> GetAsync(AccountId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_summaries.TryGetValue(id, out var summary) ? summary : null);

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _summaries.Clear();
        _lines.Clear();
        return Task.CompletedTask;
    }

    public Task ApplyAsync(RecordedEvent recorded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        if (!AccountSummaryProjection.TryGetAccount(recorded.Event, out var id))
        {
            return Task.CompletedTask;
        }

        var previous = _summaries.GetValueOrDefault(id);
        var next = AccountSummaryProjection.Apply(previous, recorded);
        _summaries[id] = next;

        // Apply hands back the same summary for an event it has already seen
        var line = ReferenceEquals(next, previous) ? null : LineFor(recorded, next);
        if (line is not null)
        {
            _lines.AddOrUpdate(id, [line], (_, lines) => lines.Add(line));
        }

        return Task.CompletedTask;
    }

    public Task<StatementPage?> GetAsync(GetStatement query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_summaries.ContainsKey(query.AccountId))
        {
            return Task.FromResult<StatementPage?>(null);
        }

        var fetched = _lines.GetValueOrDefault(query.AccountId, [])
            .Where(l => l.Version > query.AfterVersion)
            .Where(l => query.From is not { } from || l.OccurredAt >= from)
            .Where(l => query.To is not { } to || l.OccurredAt < to)
            .Take(query.PageSize + 1)
            .ToList();
        return Task.FromResult<StatementPage?>(StatementPaging.Page(fetched, query.PageSize));
    }

    private static StatementLine? LineFor(RecordedEvent recorded, AccountSummary after) => recorded.Event switch
    {
        MoneyDeposited e => new StatementLine(
            recorded.Version, StatementLineKind.Deposit, e.Amount, after.Balance, e.OccurredAt),
        MoneyWithdrawn e => new StatementLine(
            recorded.Version, StatementLineKind.Withdrawal, -e.Amount, after.Balance, e.OccurredAt),
        _ => null,
    };
}
