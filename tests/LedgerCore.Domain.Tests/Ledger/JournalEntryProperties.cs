using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Monetary;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Domain.Tests.Ledger;

public class JournalEntryProperties
{
    private const int MaxMinorUnits = 1_000_000;
    private const int MaxPostingsPerSide = 4;

    // one currency per precision the ledger supports: 0, 2 and 3 decimal places
    private static readonly Currency[] Currencies = [Currency.FromCode("JPY"), Currency.Eur, Currency.FromCode("BHD")];

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));

    private static readonly Gen<AccountId> Accounts = Gen.Elements(Enumerable.Range(0, 6).Select(_ => AccountId.New(Clock)).ToArray());

    private static readonly Arbitrary<Posting[]> BalancedEntries =
        (from currencies in Gen.SubListOf(Currencies)
         where currencies.Count > 0
         from groups in Gen.CollectToArray(currencies, BalancedPostings)
         from shuffled in Gen.Shuffle(groups.SelectMany(g => g).ToArray())
         select shuffled).ToArbitrary();

    [Property(MaxTest = 500)]
    public Property Any_balanced_set_of_postings_books() =>
        Prop.ForAll(BalancedEntries, postings =>
        {
            var entry = JournalEntry.Book(JournalEntryId.New(Clock), postings, Clock);
            return entry.Postings.SequenceEqual(postings);
        });

    [Property(MaxTest = 500)]
    public Property Debits_equal_credits_in_every_currency_of_a_booked_entry() =>
        Prop.ForAll(BalancedEntries, postings =>
        {
            var entry = JournalEntry.Book(JournalEntryId.New(Clock), postings, Clock);
            return entry.Postings
                .GroupBy(p => p.Amount.Currency)
                .All(g => Sum(g, PostingSide.Debit) == Sum(g, PostingSide.Credit));
        });

    private static Gen<IEnumerable<Posting>> BalancedPostings(Currency currency) =>
        from count in Gen.Choose(1, MaxPostingsPerSide)
        from debits in Gen.Choose(1, MaxMinorUnits).ArrayOf(count)
        from credits in Split(debits.Sum())
        from debitAccounts in Accounts.ArrayOf(debits.Length)
        from creditAccounts in Accounts.ArrayOf(credits.Length)
        select debits.Select((units, i) => Posting.Debit(debitAccounts[i], Minor(units, currency)))
            .Concat(credits.Select((units, i) => Posting.Credit(creditAccounts[i], Minor(units, currency))));

    // cuts a total into up to MaxPostingsPerSide positive pieces that add back up to it exactly
    private static Gen<int[]> Split(int total) =>
        from cuts in Gen.Choose(1, Math.Max(1, total - 1)).ArrayOf(MaxPostingsPerSide - 1)
        let points = cuts.Where(c => c < total).Distinct().Order().Prepend(0).Append(total).ToArray()
        select points.Zip(points.Skip(1), (start, end) => end - start).ToArray();

    private static Money Minor(int units, Currency currency) =>
        Money.Of(units / (decimal)Math.Pow(10, currency.MinorUnits), currency);

    private static decimal Sum(IEnumerable<Posting> postings, PostingSide side) =>
        postings.Where(p => p.Side == side).Sum(p => p.Amount.Amount);
}
