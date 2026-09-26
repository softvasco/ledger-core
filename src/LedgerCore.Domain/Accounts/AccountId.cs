using System.Diagnostics.CodeAnalysis;

namespace LedgerCore.Domain.Accounts;

/// <summary>Identity of a ledger account, so it can't be mixed up with any other Guid.</summary>
public readonly record struct AccountId
{
    private AccountId(Guid value) => Value = value;

    public Guid Value { get; }

    // v7 ids sort by creation time, which keeps inserts at the end of the event store index
    public static AccountId New(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new AccountId(Guid.CreateVersion7(clock.GetUtcNow()));
    }

    public static AccountId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("An account id can't be empty.", nameof(value))
            : new AccountId(value);

    public static AccountId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return TryParse(value, out var id)
            ? id
            : throw new ArgumentException($"'{value}' is not a valid account id.", nameof(value));
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out AccountId id)
    {
        id = default;
        if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
        {
            return false;
        }

        id = new AccountId(guid);
        return true;
    }

    public override string ToString() => Value.ToString();
}
