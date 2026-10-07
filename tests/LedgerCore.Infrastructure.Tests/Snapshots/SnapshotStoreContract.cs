using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Infrastructure.Tests.Snapshots;

public abstract class SnapshotStoreContract(ISnapshotStore<AccountSnapshot> store)
{
    protected static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stream_without_a_snapshot_loads_as_null()
    {
        Assert.Null(await store.LoadAsync(NewStream(), Token));
    }

    [Fact]
    public async Task A_saved_snapshot_loads_back_equal()
    {
        var (stream, snapshot) = Taken(version: 3, AccountStatus.Frozen, FreezeReason.CourtOrder);

        await store.SaveAsync(stream, 3, snapshot, Token);

        Assert.Equal(snapshot, await store.LoadAsync(stream, Token));
    }

    [Fact]
    public async Task A_later_snapshot_replaces_the_stored_one()
    {
        var (stream, first) = Taken(version: 2, AccountStatus.Open);
        var second = first with { Status = AccountStatus.Closed, Version = 5 };

        await store.SaveAsync(stream, 2, first, Token);
        await store.SaveAsync(stream, 5, second, Token);

        Assert.Equal(second, await store.LoadAsync(stream, Token));
    }

    // two saves can race, and the slower one may carry the older state
    [Fact]
    public async Task An_older_snapshot_never_replaces_a_newer_one()
    {
        var (stream, newer) = Taken(version: 5, AccountStatus.Closed);
        var older = newer with { Status = AccountStatus.Open, Version = 2 };

        await store.SaveAsync(stream, 5, newer, Token);
        await store.SaveAsync(stream, 2, older, Token);

        Assert.Equal(newer, await store.LoadAsync(stream, Token));
    }

    protected static StreamId NewStream() => StreamId.For("account", Guid.CreateVersion7());

    private static (StreamId Stream, AccountSnapshot Snapshot) Taken(
        long version, AccountStatus status, FreezeReason? reason = null)
    {
        var stream = NewStream();
        return (stream, new AccountSnapshot(
            AccountId.From(stream.Id), SomeIban, Currency.Eur, status, reason, Money.Of(1250.75m, Currency.Eur), version));
    }
}
