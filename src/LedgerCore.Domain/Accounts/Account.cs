using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts;

/// <summary>A ledger account and its lifecycle. Every change is recorded as an event first and applied to the state from there.</summary>
public sealed class Account : AggregateRoot<AccountId>
{
    private Account()
    {
    }

    public Iban Iban { get; private set; } = null!;

    public Currency Currency { get; private set; } = null!;

    public AccountStatus Status { get; private set; }

    public FreezeReason? FreezeReason { get; private set; }

    public static Account Open(AccountId id, Iban iban, Currency currency, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(iban);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(clock);

        var account = new Account();
        account.Raise(new AccountOpened(id, iban, currency, clock.GetUtcNow()));
        return account;
    }

    /// <summary>Rebuilds an account from its stored events. Nothing is pending afterwards.</summary>
    public static Account FromHistory(IEnumerable<IDomainEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var events = history.ToArray();
        if (events.Length == 0 || events[0] is not AccountOpened)
        {
            throw new ArgumentException("An account's history must start with AccountOpened.", nameof(history));
        }

        var account = new Account();
        account.Replay(events);
        return account;
    }

    /// <summary>Rebuilds an account from a snapshot and the events stored after it.</summary>
    public static Account FromSnapshot(AccountSnapshot snapshot, IEnumerable<IDomainEvent> laterEvents)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(laterEvents);

        var account = new Account
        {
            Id = snapshot.AccountId,
            Iban = snapshot.Iban,
            Currency = snapshot.Currency,
            Status = snapshot.Status,
            FreezeReason = snapshot.FreezeReason,
        };
        account.RestoreVersion(snapshot.Version);
        account.Replay(laterEvents);
        return account;
    }

    // a snapshot of unsaved changes could outlive a failed append and claim events that never got stored
    public AccountSnapshot ToSnapshot() =>
        PendingEvents.Count == 0
            ? new AccountSnapshot(Id, Iban, Currency, Status, FreezeReason, Version)
            : throw new InvalidOperationException("Save the pending events before taking a snapshot.");

    public Result Freeze(FreezeReason reason, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown freeze reason.");
        }

        return Change(AccountStatus.Open, "frozen", () => new AccountFrozen(Id, reason, clock.GetUtcNow()));
    }

    public Result Unfreeze(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return Change(AccountStatus.Frozen, "unfrozen", () => new AccountUnfrozen(Id, clock.GetUtcNow()));
    }

    // a frozen account has to be released first, so closing can't be used to get around a hold
    public Result Close(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return Change(AccountStatus.Open, "closed", () => new AccountClosed(Id, clock.GetUtcNow()));
    }

    private Result Change(AccountStatus requiredStatus, string action, Func<IDomainEvent> change)
    {
        if (Status != requiredStatus)
        {
            return Result.Failure(AccountErrors.InvalidState(Id, Status, action));
        }

        Raise(change());
        return Result.Success();
    }

    protected override void Apply(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case AccountOpened opened:
                Id = opened.AccountId;
                Iban = opened.Iban;
                Currency = opened.Currency;
                Status = AccountStatus.Open;
                break;
            case AccountFrozen frozen:
                Status = AccountStatus.Frozen;
                FreezeReason = frozen.Reason;
                break;
            case AccountUnfrozen:
                Status = AccountStatus.Open;
                FreezeReason = null;
                break;
            case AccountClosed:
                Status = AccountStatus.Closed;
                break;
            default:
                throw new InvalidOperationException($"{domainEvent.GetType().Name} does not belong to an account.");
        }
    }
}
