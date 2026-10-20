using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Domain.Transfers;
using LedgerCore.Domain.Transfers.Events;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Domain.Tests.Transfers;

public class TransferTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 21, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly AccountId _source;
    private readonly AccountId _destination;

    public TransferTests()
    {
        _source = AccountId.New(_clock);
        _destination = AccountId.New(_clock);
    }

    [Fact]
    public void A_request_records_both_accounts_and_the_amount()
    {
        var id = TransferId.New(_clock);

        var transfer = Transfer.Request(id, _source, _destination, Money.Of(40m, Currency.Eur), _clock);

        Assert.Equal(
            new TransferRequested(id, _source, _destination, Money.Of(40m, Currency.Eur), Start),
            Assert.Single(transfer.PendingEvents));
        Assert.Equal(TransferStatus.Requested, transfer.Status);
        Assert.False(transfer.IsFinished);
    }

    [Fact]
    public void A_transfer_to_the_same_account_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Transfer.Request(TransferId.New(_clock), _source, _source, Money.Of(40m, Currency.Eur), _clock));
    }

    [Fact]
    public void A_zero_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Transfer.Request(TransferId.New(_clock), _source, _destination, Money.Zero(Currency.Eur), _clock));
    }

    [Fact]
    public void Debit_then_complete_finishes_the_transfer()
    {
        var transfer = Requested();

        transfer.SourceDebited(_clock);
        transfer.Complete(_clock);

        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.True(transfer.IsFinished);
    }

    [Fact]
    public void A_refused_debit_fails_the_transfer_with_the_reason()
    {
        var transfer = Requested();

        transfer.Fail(AccountErrors.InsufficientFundsCode, _clock);

        Assert.Equal(TransferStatus.Failed, transfer.Status);
        Assert.Equal(AccountErrors.InsufficientFundsCode, transfer.Reason);
    }

    [Fact]
    public void A_refused_credit_after_the_debit_is_compensated()
    {
        var transfer = Requested();
        transfer.SourceDebited(_clock);

        transfer.Compensate(AccountErrors.InvalidStateCode, _clock);

        Assert.Equal(TransferStatus.Compensated, transfer.Status);
        Assert.Equal(AccountErrors.InvalidStateCode, transfer.Reason);
    }

    [Fact]
    public void Completing_before_the_debit_is_a_bug()
    {
        var transfer = Requested();

        Assert.Throws<InvalidOperationException>(() => transfer.Complete(_clock));
    }

    [Fact]
    public void Failing_after_the_debit_is_a_bug_because_the_money_has_to_go_back()
    {
        var transfer = Requested();
        transfer.SourceDebited(_clock);

        Assert.Throws<InvalidOperationException>(() => transfer.Fail(AccountErrors.InvalidStateCode, _clock));
    }

    [Fact]
    public void A_finished_transfer_takes_no_more_steps()
    {
        var transfer = Requested();
        transfer.SourceDebited(_clock);
        transfer.Complete(_clock);

        Assert.Throws<InvalidOperationException>(() => transfer.Compensate(AccountErrors.InvalidStateCode, _clock));
    }

    [Fact]
    public void Replaying_the_history_gives_the_same_state()
    {
        var transfer = Requested();
        transfer.SourceDebited(_clock);
        transfer.Compensate(AccountErrors.InvalidStateCode, _clock);

        var replayed = Transfer.FromHistory(transfer.PendingEvents);

        Assert.Equal(transfer.Id, replayed.Id);
        Assert.Equal(transfer.Amount, replayed.Amount);
        Assert.Equal(TransferStatus.Compensated, replayed.Status);
        Assert.Equal(3, replayed.Version);
        Assert.Empty(replayed.PendingEvents);
    }

    private Transfer Requested() =>
        Transfer.Request(TransferId.New(_clock), _source, _destination, Money.Of(40m, Currency.Eur), _clock);
}
