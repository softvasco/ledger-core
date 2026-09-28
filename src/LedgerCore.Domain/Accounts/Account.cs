using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts;

/// <summary>A ledger account and its lifecycle. Every change is recorded as an event first and applied to the state from there.</summary>
public sealed class Account
{
    private readonly List<IDomainEvent> _pendingEvents = [];

    private Account()
    {
    }

    public AccountId Id { get; private set; }

    public Iban Iban { get; private set; } = null!;

    public Currency Currency { get; private set; } = null!;

    public AccountStatus Status { get; private set; }

    public FreezeReason? FreezeReason { get; private set; }

    /// <summary>Events raised since the account was loaded, in the order they happened.</summary>
    public IReadOnlyList<IDomainEvent> PendingEvents => _pendingEvents;

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

        var account = new Account();
        var first = true;
        foreach (var @event in history)
        {
            if (first && @event is not AccountOpened)
            {
                throw new ArgumentException("An account's history must start with AccountOpened.", nameof(history));
            }

            account.Apply(@event);
            first = false;
        }

        return first
            ? throw new ArgumentException("An account's history can't be empty.", nameof(history))
            : account;
    }

    public void Freeze(FreezeReason reason, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown freeze reason.");
        }

        EnsureStatus(AccountStatus.Open, "frozen");
        Raise(new AccountFrozen(Id, reason, clock.GetUtcNow()));
    }

    public void Unfreeze(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        EnsureStatus(AccountStatus.Frozen, "unfrozen");
        Raise(new AccountUnfrozen(Id, clock.GetUtcNow()));
    }

    // a frozen account has to be released first, so closing can't be used to get around a hold
    public void Close(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        EnsureStatus(AccountStatus.Open, "closed");
        Raise(new AccountClosed(Id, clock.GetUtcNow()));
    }

    private void EnsureStatus(AccountStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new AccountStateException(Id, Status, action);
        }
    }

    private void Raise(IDomainEvent @event)
    {
        Apply(@event);
        _pendingEvents.Add(@event);
    }

    private void Apply(IDomainEvent @event)
    {
        switch (@event)
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
                throw new InvalidOperationException($"{@event.GetType().Name} does not belong to an account.");
        }
    }
}
