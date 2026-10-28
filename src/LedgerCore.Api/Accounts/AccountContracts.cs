using LedgerCore.Application.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Api.Accounts;

public sealed record OpenAccountRequest(string? Iban, string? Currency);

public sealed record AmountRequest(decimal Amount, string? Currency);

public sealed record AccountOpenedResponse(Guid Id);

public sealed record BalanceResponse(decimal Balance, string Currency)
{
    public static BalanceResponse From(Money balance) => new(balance.Amount, balance.Currency.Code);
}

public sealed record AccountResponse(Guid Id, string Iban, string Currency, string Status, decimal Balance, long Version)
{
    public static AccountResponse From(AccountSummary summary) => new(
        summary.Id.Value,
        summary.Iban.Value,
        summary.Currency.Code,
        summary.Status.ToString(),
        summary.Balance.Amount,
        summary.Version);
}

public sealed record StatementLineResponse(
    long Version, string Kind, decimal Amount, decimal BalanceAfter, string Currency, DateTimeOffset OccurredAt);

/// <summary>Send <see cref="NextAfter"/> back as after for more; null on the last page.</summary>
public sealed record StatementResponse(IReadOnlyList<StatementLineResponse> Lines, long? NextAfter)
{
    public static StatementResponse From(StatementPage page) => new(
        [.. page.Lines.Select(l => new StatementLineResponse(
            l.Version,
            l.Kind == StatementLineKind.Deposit ? "deposit" : "withdrawal",
            l.Amount.Amount,
            l.BalanceAfter.Amount,
            l.Amount.Currency.Code,
            l.OccurredAt))],
        page.NextAfterVersion);
}
