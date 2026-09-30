using LedgerCore.Domain.Abstractions;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Ledger;

public static class LedgerErrors
{
    public const string TooFewPostingsCode = "ledger.too_few_postings";
    public const string UnbalancedCode = "ledger.unbalanced";

    public static DomainError TooFewPostings(int count) =>
        new(TooFewPostingsCode, $"A journal entry needs at least one debit and one credit, got {count} posting(s).");

    public static DomainError Unbalanced(Money debits, Money credits) =>
        new(UnbalancedCode, $"Debits of {debits} don't match credits of {credits}.");
}
