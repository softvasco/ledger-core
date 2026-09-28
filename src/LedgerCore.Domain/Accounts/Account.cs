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
            default:
                throw new InvalidOperationException($"{@event.GetType().Name} does not belong to an account.");
        }
    }
}
