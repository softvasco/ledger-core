using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Ledger;

public sealed class UnbalancedEntryException : InvalidOperationException
{
    public UnbalancedEntryException()
    {
    }

    public UnbalancedEntryException(string message)
        : base(message)
    {
    }

    public UnbalancedEntryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UnbalancedEntryException(Money debits, Money credits)
        : base($"Debits of {debits} don't match credits of {credits}.")
    {
    }
}
