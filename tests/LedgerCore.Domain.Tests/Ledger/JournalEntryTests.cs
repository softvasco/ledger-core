using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.Domain.Monetary;
using Microsoft.Extensions.Time.Testing;

namespace LedgerCore.Domain.Tests.Ledger;

public class JournalEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 14, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AccountId _cash;
    private readonly AccountId _customer;
    private readonly AccountId _fees;

    public JournalEntryTests()
    {
        _cash = AccountId.New(_clock);
        _customer = AccountId.New(_clock);
        _fees = AccountId.New(_clock);
    }

    [Fact]
    public void Books_a_balanced_entry_with_its_postings_and_time()
    {
        var postings = new[] { Posting.Debit(_cash, Eur(100m)), Posting.Credit(_customer, Eur(100m)) };

        var entry = JournalEntry.Book(JournalEntryId.New(_clock), postings, _clock);

        Assert.Equal(postings, entry.Postings);
        Assert.Equal(Now, entry.BookedAt);
    }

    [Fact]
    public void One_debit_can_be_split_across_several_credits()
    {
        var entry = JournalEntry.Book(
            JournalEntryId.New(_clock),
            [Posting.Debit(_customer, Eur(10.00m)), Posting.Credit(_cash, Eur(9.75m)), Posting.Credit(_fees, Eur(0.25m))],
            _clock);

        Assert.Equal(3, entry.Postings.Count);
    }

    [Fact]
    public void Refuses_an_entry_where_debits_and_credits_differ()
    {
        Posting[] postings = [Posting.Debit(_cash, Eur(100m)), Posting.Credit(_customer, Eur(99.99m))];

        var error = Assert.Throws<UnbalancedEntryException>(() => JournalEntry.Book(JournalEntryId.New(_clock), postings, _clock));
        Assert.Contains("EUR 100.00", error.Message, StringComparison.Ordinal);
        Assert.Contains("EUR 99.99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_currency_has_to_balance_on_its_own()
    {
        var fxDesk = AccountId.New(_clock);

        var balanced = JournalEntry.Book(
            JournalEntryId.New(_clock),
            [
                Posting.Debit(_customer, Eur(100m)), Posting.Credit(fxDesk, Eur(100m)),
                Posting.Debit(fxDesk, Usd(108m)), Posting.Credit(_customer, Usd(108m)),
            ],
            _clock);

        Assert.Equal(4, balanced.Postings.Count);
        Assert.Throws<UnbalancedEntryException>(() => JournalEntry.Book(
            JournalEntryId.New(_clock),
            [Posting.Debit(_customer, Eur(100m)), Posting.Credit(fxDesk, Usd(100m))],
            _clock));
    }

    [Fact]
    public void Needs_at_least_two_postings()
    {
        Assert.Throws<ArgumentException>(() => JournalEntry.Book(JournalEntryId.New(_clock), [Posting.Debit(_cash, Eur(1m))], _clock));
        Assert.Throws<ArgumentException>(() => JournalEntry.Book(JournalEntryId.New(_clock), [], _clock));
    }

    [Fact]
    public void Later_changes_to_the_source_list_do_not_reach_the_entry()
    {
        var postings = new List<Posting> { Posting.Debit(_cash, Eur(5m)), Posting.Credit(_customer, Eur(5m)) };
        var entry = JournalEntry.Book(JournalEntryId.New(_clock), postings, _clock);

        postings.Add(Posting.Debit(_fees, Eur(1m)));

        Assert.Equal(2, entry.Postings.Count);
    }

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.Eur);

    private static Money Usd(decimal amount) => Money.Of(amount, Currency.Usd);
}
