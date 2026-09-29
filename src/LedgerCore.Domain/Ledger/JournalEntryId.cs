namespace LedgerCore.Domain.Ledger;

public readonly record struct JournalEntryId
{
    private JournalEntryId(Guid value) => Value = value;

    public Guid Value { get; }

    public static JournalEntryId New(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new JournalEntryId(Guid.CreateVersion7(clock.GetUtcNow()));
    }

    public static JournalEntryId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A journal entry id can't be empty.", nameof(value))
            : new JournalEntryId(value);

    public override string ToString() => Value.ToString();
}
