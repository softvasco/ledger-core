namespace LedgerCore.Domain.Abstractions;

/// <summary>A business rule said no. The code is stable for callers to branch on; the message is for people.</summary>
public sealed record DomainError(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}
