using LedgerCore.Domain.Monetary;

namespace LedgerCore.Domain.Accounts;

/// <summary>An account as of <see cref="Version"/>, so a long stream needs no full replay.</summary>
public sealed record AccountSnapshot(
    AccountId AccountId,
    Iban Iban,
    Currency Currency,
    AccountStatus Status,
    FreezeReason? FreezeReason,
    long Version);
