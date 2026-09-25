namespace LedgerCore.Domain.Monetary;

public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException()
    {
    }

    public CurrencyMismatchException(string message)
        : base(message)
    {
    }

    public CurrencyMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CurrencyMismatchException(Currency expected, Currency actual)
        : base($"Cannot combine {expected} with {actual}.")
    {
    }
}
