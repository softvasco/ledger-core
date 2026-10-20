using System.Diagnostics.CodeAnalysis;

namespace LedgerCore.Domain.Transfers;

/// <summary>Identity of a transfer between two accounts.</summary>
public readonly record struct TransferId
{
    private TransferId(Guid value) => Value = value;

    public Guid Value { get; }

    public static TransferId New(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new TransferId(Guid.CreateVersion7(clock.GetUtcNow()));
    }

    public static TransferId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A transfer id can't be empty.", nameof(value))
            : new TransferId(value);

    // route binding finds this by name, so a malformed id in the URL is a 400 before any handler runs
    public static bool TryParse([NotNullWhen(true)] string? value, out TransferId id)
    {
        id = default;
        if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
        {
            return false;
        }

        id = new TransferId(guid);
        return true;
    }

    public override string ToString() => Value.ToString();
}
