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

    public override string ToString() => Value.ToString();
}
