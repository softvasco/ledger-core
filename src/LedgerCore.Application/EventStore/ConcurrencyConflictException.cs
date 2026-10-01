namespace LedgerCore.Application.EventStore;

// not a Result (ADR-0003): nobody asked for a business rule here, the command just has to reload and run again
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConcurrencyConflictException(StreamId stream, long expectedVersion, long actualVersion)
        : base($"Expected {stream} at version {expectedVersion}, but it is at {actualVersion}.")
    {
        Stream = stream;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public StreamId Stream { get; }

    public long ExpectedVersion { get; }

    public long ActualVersion { get; }
}
