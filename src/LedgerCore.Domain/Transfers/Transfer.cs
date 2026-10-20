using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Monetary;
using LedgerCore.Domain.Transfers.Events;

namespace LedgerCore.Domain.Transfers;

/// <summary>A move of money between two accounts, done in steps that can be undone.</summary>
public sealed class Transfer : AggregateRoot<TransferId>
{
    private Transfer()
    {
    }

    public AccountId Source { get; private set; }

    public AccountId Destination { get; private set; }

    public Money Amount { get; private set; } = null!;

    public TransferStatus Status { get; private set; }

    /// <summary>The error code that stopped it, if it failed or was compensated.</summary>
    public string? Reason { get; private set; }

    public bool IsFinished => Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Compensated;

    public static Transfer Request(TransferId id, AccountId source, AccountId destination, Money amount, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        if (source == destination)
        {
            throw new ArgumentException("A transfer needs two different accounts.", nameof(destination));
        }

        if (amount.IsNegative || amount.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be greater than zero.");
        }

        var transfer = new Transfer();
        transfer.Raise(new TransferRequested(id, source, destination, amount, clock.GetUtcNow()));
        return transfer;
    }

    public static Transfer FromHistory(IEnumerable<IDomainEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var events = history.ToArray();
        if (events.Length == 0 || events[0] is not TransferRequested)
        {
            throw new ArgumentException("A transfer's history must start with TransferRequested.", nameof(history));
        }

        var transfer = new Transfer();
        transfer.Replay(events);
        return transfer;
    }

    public void SourceDebited(TimeProvider clock) =>
        Move(TransferStatus.Requested, () => new TransferSourceDebited(Id, Now(clock)));

    public void Complete(TimeProvider clock) =>
        Move(TransferStatus.SourceDebited, () => new TransferCompleted(Id, Now(clock)));

    // the source refused, so no money moved and there is nothing to undo
    public void Fail(string reason, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Move(TransferStatus.Requested, () => new TransferFailed(Id, reason, Now(clock)));
    }

    // the destination refused after the source paid, so the money has gone back to the source
    public void Compensate(string reason, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Move(TransferStatus.SourceDebited, () => new TransferCompensated(Id, reason, Now(clock)));
    }

    private static DateTimeOffset Now(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return clock.GetUtcNow();
    }

    // a step out of order is a bug in whoever drives the transfer, not a business outcome
    private void Move(TransferStatus from, Func<IDomainEvent> step)
    {
        if (Status != from)
        {
            throw new InvalidOperationException($"Transfer {Id} is {Status}, not {from}.");
        }

        Raise(step());
    }

    protected override void Apply(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case TransferRequested requested:
                Id = requested.TransferId;
                Source = requested.Source;
                Destination = requested.Destination;
                Amount = requested.Amount;
                Status = TransferStatus.Requested;
                break;
            case TransferSourceDebited:
                Status = TransferStatus.SourceDebited;
                break;
            case TransferCompleted:
                Status = TransferStatus.Completed;
                break;
            case TransferFailed failed:
                Status = TransferStatus.Failed;
                Reason = failed.Reason;
                break;
            case TransferCompensated compensated:
                Status = TransferStatus.Compensated;
                Reason = compensated.Reason;
                break;
            default:
                throw new InvalidOperationException($"{domainEvent.GetType().Name} does not belong to a transfer.");
        }
    }
}
