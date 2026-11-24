using System.Net;
using System.Net.Http.Json;

namespace LedgerCore.Admin;

public sealed record AccountRow(Guid Id, string Iban, string Currency, string Status, decimal Balance, long Version);

public sealed record AccountList(IReadOnlyList<AccountRow> Accounts, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed record AccountEvent(
    long Version, string Type, DateTimeOffset OccurredAt, decimal? Amount, string? Currency, string? Detail);

public sealed record AccountHistory(IReadOnlyList<AccountEvent> Events, long? NextAfter);

/// <summary>The ledger API as the back office reads it.</summary>
public sealed class LedgerApiClient(HttpClient http)
{
    public async Task<AccountList> ListAccountsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var list = await http.GetFromJsonAsync<AccountList>($"accounts?page={page}&pageSize={pageSize}", cancellationToken);
        return list ?? throw new InvalidOperationException("The ledger API answered the account list with an empty body.");
    }

    /// <summary>Null on a 404, which for a new account can just mean the read model hasn't caught up.</summary>
    public Task<AccountRow?> GetAccountAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<AccountRow>($"accounts/{id}", cancellationToken);

    /// <summary>Null when the account has no events at all.</summary>
    public Task<AccountHistory?> GetHistoryAsync(Guid id, long after, int pageSize, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<AccountHistory>($"accounts/{id}/history?after={after}&pageSize={pageSize}", cancellationToken);

    private async Task<T?> GetOrNullAsync<T>(string uri, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await http.GetAsync(uri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException($"The ledger API answered {uri} with an empty body.");
    }
}
