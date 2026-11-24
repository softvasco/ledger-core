using System.Net.Http.Json;

namespace LedgerCore.Admin;

public sealed record AccountRow(Guid Id, string Iban, string Currency, string Status, decimal Balance, long Version);

public sealed record AccountList(IReadOnlyList<AccountRow> Accounts, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>The ledger API as the back office reads it.</summary>
public sealed class LedgerApiClient(HttpClient http)
{
    public async Task<AccountList> ListAccountsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var list = await http.GetFromJsonAsync<AccountList>($"accounts?page={page}&pageSize={pageSize}", cancellationToken);
        return list ?? throw new InvalidOperationException("The ledger API answered the account list with an empty body.");
    }
}
