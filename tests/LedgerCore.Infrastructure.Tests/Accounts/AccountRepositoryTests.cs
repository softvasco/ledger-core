using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Infrastructure.EventStore;
using LedgerCore.Infrastructure.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace LedgerCore.Infrastructure.Tests.Accounts;

public class AccountRepositoryTests
{
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly InMemoryEventStore _events = new();
    private readonly InMemorySnapshotStore<AccountSnapshot> _snapshots = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_saved_account_loads_back_with_its_state_and_version()
    {
        var repository = Repository(every: 100);
        var account = await OpenAndFlip(repository, flips: 1);

        var loaded = await repository.LoadAsync(account.Id, Token);

        Assert.NotNull(loaded);
        Assert.Equal(account.ToSnapshot(), loaded.ToSnapshot());
    }

    [Fact]
    public async Task An_unknown_account_loads_as_null()
    {
        Assert.Null(await Repository(every: 100).LoadAsync(AccountId.New(_clock), Token));
    }

    [Fact]
    public async Task A_snapshot_is_taken_each_time_the_stream_passes_the_threshold()
    {
        var repository = Repository(every: 3);
        var account = await OpenAndFlip(repository, flips: 4);

        var snapshot = await _snapshots.LoadAsync(StreamId.For("account", account.Id.Value), Token);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.Version);
        Assert.Equal(5, account.Version);
    }

    [Fact]
    public async Task Loading_with_or_without_snapshots_gives_the_same_account()
    {
        var repository = Repository(every: 2);
        var account = await OpenAndFlip(repository, flips: 5);

        var withSnapshots = await repository.LoadAsync(account.Id, Token);
        _snapshots.Clear();
        var fromEventsOnly = await repository.LoadAsync(account.Id, Token);

        Assert.Equal(fromEventsOnly!.ToSnapshot(), withSnapshots!.ToSnapshot());
        Assert.Equal(6, withSnapshots.Version);
    }

    [Fact]
    public async Task A_failed_snapshot_does_not_fail_the_save()
    {
        var repository = new AccountRepository(
            _events, new BrokenSnapshotStore(), new SnapshotPolicy(1), NullLogger<AccountRepository>.Instance);
        var account = Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);

        await repository.SaveAsync(account, Token);

        Assert.Empty(account.PendingEvents);
        Assert.Equal(1, (await repository.LoadAsync(account.Id, Token))!.Version);
    }

    [Fact]
    public async Task Saving_a_stale_account_is_a_conflict()
    {
        var repository = Repository(every: 100);
        var account = await OpenAndFlip(repository, flips: 0);
        var first = await repository.LoadAsync(account.Id, Token);
        var second = await repository.LoadAsync(account.Id, Token);
        first!.Close(_clock);
        second!.Freeze(FreezeReason.CourtOrder, _clock);

        await repository.SaveAsync(first, Token);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.SaveAsync(second, Token));
    }

    private AccountRepository Repository(int every) =>
        new(_events, _snapshots, new SnapshotPolicy(every), NullLogger<AccountRepository>.Instance);

    // each flip is a freeze or an unfreeze saved on its own, so the stream grows one event per save
    private async Task<Account> OpenAndFlip(AccountRepository repository, int flips)
    {
        var account = Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);
        await repository.SaveAsync(account, Token);
        for (var i = 0; i < flips; i++)
        {
            var result = account.Status == AccountStatus.Open
                ? account.Freeze(FreezeReason.SuspectedFraud, _clock)
                : account.Unfreeze(_clock);
            Assert.True(result.IsSuccess);
            await repository.SaveAsync(account, Token);
        }

        return account;
    }

    private sealed class BrokenSnapshotStore : ISnapshotStore<AccountSnapshot>
    {
        public Task<AccountSnapshot?> LoadAsync(StreamId stream, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(null);

        public Task SaveAsync(
            StreamId stream, long version, AccountSnapshot snapshot, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("snapshot storage is down");
    }
}
