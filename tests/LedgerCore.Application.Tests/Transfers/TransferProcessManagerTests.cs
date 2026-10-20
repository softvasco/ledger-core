using System.Runtime.CompilerServices;
using LedgerCore.Application.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Application.Snapshots;
using LedgerCore.Application.Transfers;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Domain.Transfers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Application.Tests.Transfers;

public class TransferProcessManagerTests
{
    private static readonly Iban SomeIban = Iban.Parse("PT50 0002 0123 1234 5678 9015 4");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.Zero));
    private readonly AccountRepository _accounts;
    private readonly TransferRepository _transfers;
    private readonly StartTransferHandler _handler;

    public TransferProcessManagerTests()
    {
        var events = new ListEventStore();
        _accounts = new AccountRepository(events, new NoSnapshots(), SnapshotPolicy.Default, NullLogger<AccountRepository>.Instance);
        _transfers = new TransferRepository(events);
        _handler = new StartTransferHandler(_transfers, Manager, _clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private TransferProcessManager Manager => new(_accounts, _transfers, _clock);

    [Fact]
    public async Task A_transfer_debits_the_source_and_credits_the_destination()
    {
        var source = await OpenWith(100m);
        var destination = await OpenWith(0m);

        var result = await _handler.HandleAsync(Start(source, destination, 40m), Token);

        Assert.Equal(TransferStatus.Completed, result.Value);
        Assert.Equal(Eur(60m), await BalanceOf(source));
        Assert.Equal(Eur(40m), await BalanceOf(destination));
    }

    [Fact]
    public async Task A_refused_debit_fails_the_transfer_and_moves_nothing()
    {
        var source = await OpenWith(10m);
        var destination = await OpenWith(0m);
        var command = Start(source, destination, 40m);

        var result = await _handler.HandleAsync(command, Token);

        Assert.Equal(AccountErrors.InsufficientFundsCode, result.Error?.Code);
        Assert.Equal(Eur(10m), await BalanceOf(source));
        Assert.Equal(TransferStatus.Failed, (await _transfers.LoadAsync(command.TransferId, Token))!.Status);
    }

    [Fact]
    public async Task A_refused_credit_puts_the_money_back_on_the_source()
    {
        var source = await OpenWith(100m);
        var destination = await OpenWith(0m);
        await Change(destination, a => a.Close(_clock));
        var command = Start(source, destination, 40m);

        var result = await _handler.HandleAsync(command, Token);

        Assert.Equal(AccountErrors.InvalidStateCode, result.Error?.Code);
        Assert.Equal(Eur(100m), await BalanceOf(source));
        Assert.Equal(TransferStatus.Compensated, (await _transfers.LoadAsync(command.TransferId, Token))!.Status);
    }

    [Fact]
    public async Task A_transfer_stopped_after_the_debit_resumes_with_the_credit()
    {
        var source = await OpenWith(100m);
        var destination = await OpenWith(0m);
        var transfer = Transfer.Request(TransferId.New(_clock), source, destination, Eur(40m), _clock);
        await Change(source, a => a.Withdraw(Eur(40m), _clock));
        transfer.SourceDebited(_clock);
        await _transfers.SaveAsync(transfer, Token);

        await Manager.RunAsync((await _transfers.LoadAsync(transfer.Id, Token))!, Token);

        Assert.Equal(Eur(60m), await BalanceOf(source));
        Assert.Equal(Eur(40m), await BalanceOf(destination));
        Assert.Equal(TransferStatus.Completed, (await _transfers.LoadAsync(transfer.Id, Token))!.Status);
    }

    [Fact]
    public async Task When_the_refund_is_refused_too_the_transfer_stays_open()
    {
        var source = await OpenWith(100m);
        var destination = await OpenWith(0m);
        await Change(destination, a => a.Close(_clock));
        var transfer = Transfer.Request(TransferId.New(_clock), source, destination, Eur(100m), _clock);
        await Change(source, a => a.Withdraw(Eur(100m), _clock));
        await Change(source, a => a.Close(_clock));
        transfer.SourceDebited(_clock);
        await _transfers.SaveAsync(transfer, Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Manager.RunAsync(transfer, Token));

        Assert.Equal(TransferStatus.SourceDebited, (await _transfers.LoadAsync(transfer.Id, Token))!.Status);
    }

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private StartTransfer Start(AccountId source, AccountId destination, decimal amount) =>
        new(TransferId.New(_clock), source, destination, Eur(amount));

    private async Task<AccountId> OpenWith(decimal balance)
    {
        var account = Account.Open(AccountId.New(_clock), SomeIban, Currency.Eur, _clock);
        if (balance > 0)
        {
            account.Deposit(Eur(balance), _clock);
        }

        await _accounts.SaveAsync(account, Token);
        return account.Id;
    }

    private async Task Change(AccountId id, Func<Account, Result> change)
    {
        var account = (await _accounts.LoadAsync(id, Token))!;
        Assert.True(change(account).IsSuccess);
        await _accounts.SaveAsync(account, Token);
    }

    private async Task<Money> BalanceOf(AccountId id) => (await _accounts.LoadAsync(id, Token))!.Balance;

    private sealed class ListEventStore : IEventStore
    {
        private readonly List<RecordedEvent> _all = [];

        public Task<long> AppendAsync(StreamId stream, long expectedVersion, IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken = default)
        {
            var version = _all.Count(e => e.StreamId == stream);
            if (version != expectedVersion)
            {
                throw new ConcurrencyConflictException(stream, expectedVersion, version);
            }

            foreach (var @event in events)
            {
                _all.Add(new RecordedEvent(stream, ++version, _all.Count + 1, @event));
            }

            return Task.FromResult((long)version);
        }

        public async IAsyncEnumerable<RecordedEvent> ReadStreamAsync(
            StreamId stream, long afterVersion = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (var recorded in _all.Where(e => e.StreamId == stream && e.Version > afterVersion).ToList())
            {
                yield return recorded;
            }
        }

        public IAsyncEnumerable<RecordedEvent> ReadAllAsync(long afterPosition = 0, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoSnapshots : ISnapshotStore<AccountSnapshot>
    {
        public Task<AccountSnapshot?> LoadAsync(StreamId stream, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshot?>(null);

        public Task SaveAsync(StreamId stream, long version, AccountSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
