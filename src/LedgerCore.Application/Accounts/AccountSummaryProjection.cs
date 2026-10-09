using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Accounts.Events;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Application.Accounts;

/// <summary>Folds account events into an <see cref="AccountSummary"/>, for any store.</summary>
public static class AccountSummaryProjection
{
    /// <summary>The account an event belongs to, or false if it isn't an account event.</summary>
    public static bool TryGetAccount(IDomainEvent domainEvent, out AccountId id)
    {
        id = domainEvent switch
        {
            AccountOpened e => e.AccountId,
            MoneyDeposited e => e.AccountId,
            MoneyWithdrawn e => e.AccountId,
            AccountFrozen e => e.AccountId,
            AccountUnfrozen e => e.AccountId,
            AccountClosed e => e.AccountId,
            _ => default,
        };
        return id != default;
    }

    public static AccountSummary Apply(AccountSummary? current, RecordedEvent recorded)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        // a reader that restarts from an older checkpoint sees events it already applied
        if (current is not null && recorded.Version <= current.Version)
        {
            return current;
        }

        if (recorded.Event is AccountOpened opened)
        {
            return new AccountSummary(
                opened.AccountId, opened.Iban, opened.Currency, AccountStatus.Open, Money.Zero(opened.Currency), recorded.Version);
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                $"{recorded.Event.GetType().Name} at {recorded.StreamId} v{recorded.Version} arrived before AccountOpened.");
        }

        var next = recorded.Event switch
        {
            MoneyDeposited e => current with { Balance = current.Balance + e.Amount },
            MoneyWithdrawn e => current with { Balance = current.Balance - e.Amount },
            AccountFrozen => current with { Status = AccountStatus.Frozen },
            AccountUnfrozen => current with { Status = AccountStatus.Open },
            AccountClosed => current with { Status = AccountStatus.Closed },
            _ => throw new InvalidOperationException($"{recorded.Event.GetType().Name} does not belong to an account."),
        };
        return next with { Version = recorded.Version };
    }
}
