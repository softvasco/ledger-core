using LedgerCore.Application.Accounts;
using LedgerCore.Domain.Monetary;

namespace LedgerCore.Api.Accounts;

public sealed record OpenAccountRequest(string? Iban, string? Currency);

public sealed record AmountRequest(decimal Amount, string? Currency);

public sealed record AccountOpenedResponse(Guid Id);

public sealed record BalanceResponse(decimal Balance, string Currency)
{
    public static BalanceResponse From(Money balance) => new(balance.ForResponse(), balance.Currency.Code);
}

public sealed record AccountResponse(Guid Id, string Iban, string Currency, string Status, decimal Balance, long Version)
{
    public static AccountResponse From(AccountSummary summary) => new(
        summary.Id.Value,
        summary.Iban.Value,
        summary.Currency.Code,
        summary.Status.ToString(),
        summary.Balance.ForResponse(),
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
            l.Amount.ForResponse(),
            l.BalanceAfter.ForResponse(),
            l.Amount.Currency.Code,
            l.OccurredAt))],
        page.NextAfterVersion);
}

public sealed record AccountHistoryEntryResponse(
    long Version, string Type, DateTimeOffset OccurredAt, decimal? Amount, string? Currency, string? Detail);

/// <summary>Type is the stored event's name. Send <see cref="NextAfter"/> back as after for more; null on the last page.</summary>
public sealed record AccountHistoryResponse(IReadOnlyList<AccountHistoryEntryResponse> Events, long? NextAfter)
{
    public static AccountHistoryResponse From(AccountHistoryPage page) => new(
        [.. page.Entries.Select(e => new AccountHistoryEntryResponse(
            e.Version, e.Type, e.OccurredAt, e.Amount?.ForResponse(), e.Amount?.Currency.Code, e.Detail))],
        page.NextAfterVersion);
}

public sealed record AccountListResponse(IReadOnlyList<AccountResponse> Accounts, int Page, int PageSize, int Total)
{
    public static AccountListResponse From(AccountListPage page) => new(
        [.. page.Accounts.Select(AccountResponse.From)], page.Page, page.PageSize, page.Total);
}
