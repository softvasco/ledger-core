using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Ledger;

/// <summary>A set of postings booked together. Debits equal credits in every currency, or the entry doesn't exist.</summary>
public sealed class JournalEntry
{
    private const int MinimumPostings = 2;

    private JournalEntry(JournalEntryId id, IReadOnlyList<Posting> postings, DateTimeOffset bookedAt)
    {
        Id = id;
        Postings = postings;
        BookedAt = bookedAt;
    }

    public JournalEntryId Id { get; }

    public IReadOnlyList<Posting> Postings { get; }

    public DateTimeOffset BookedAt { get; }

    public static JournalEntry Book(JournalEntryId id, IEnumerable<Posting> postings, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(postings);
        ArgumentNullException.ThrowIfNull(clock);
        if (id == default)
        {
            throw new ArgumentException("A journal entry needs an id.", nameof(id));
        }

        var lines = postings.ToArray();
        if (lines.Length < MinimumPostings)
        {
            throw new ArgumentException("A journal entry needs at least one debit and one credit.", nameof(postings));
        }

        // an FX trade is one entry with two currencies, and each currency has to balance on its own
        foreach (var currency in lines.GroupBy(p => p.Amount.Currency))
        {
            var debits = Total(currency, PostingSide.Debit, currency.Key);
            var credits = Total(currency, PostingSide.Credit, currency.Key);
            if (debits != credits)
            {
                throw new UnbalancedEntryException(debits, credits);
            }
        }

        return new JournalEntry(id, Array.AsReadOnly(lines), clock.GetUtcNow());
    }

    private static Money Total(IEnumerable<Posting> postings, PostingSide side, Currency currency) =>
        postings.Where(p => p.Side == side).Aggregate(Money.Zero(currency), (sum, p) => sum + p.Amount);
}
