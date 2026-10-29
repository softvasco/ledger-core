using System.Net;
using System.Net.Http.Json;
using LedgerCore.Api.Accounts;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Api.Tests;

/// <summary>The API against a real database, restarted between requests.</summary>
public abstract class StoredApiContract(string connectionString, string provider)
{
    private const string SomeIban = "PT50 0002 0123 1234 5678 9015 4";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Money_moved_through_the_api_is_still_there_after_a_restart()
    {
        Guid account;
        await using (var first = StartApi())
        {
            using var client = first.CreateClient();
            account = await OpenAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/accounts/{account}/deposits", new AmountRequest(100m, "EUR"), Token)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/accounts/{account}/withdrawals", new AmountRequest(30m, "EUR"), Token)).StatusCode);
        }

        await using var second = StartApi();
        Assert.Equal(70m, await BalanceAsync(second, account));
    }

    [Fact]
    public async Task A_retry_after_a_restart_gets_the_stored_response()
    {
        Guid account;
        string firstBody;
        await using (var first = StartApi())
        {
            using var client = first.CreateClient();
            account = await OpenAsync(client);
            var response = await DepositAsync(client, account, "restart-1");
            firstBody = await response.Content.ReadAsStringAsync(Token);
        }

        await using var second = StartApi();
        using var retryClient = second.CreateClient();
        var retry = await DepositAsync(retryClient, account, "restart-1");

        Assert.True(retry.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(firstBody, await retry.Content.ReadAsStringAsync(Token));
        Assert.Equal(50m, await BalanceAsync(second, account));
    }

    [Fact]
    public async Task Amounts_come_back_with_the_currency_minor_units_whatever_the_storage()
    {
        await using var api = StartApi();
        using var client = api.CreateClient();
        var account = await OpenAsync(client);
        await client.PostAsJsonAsync($"/accounts/{account}/deposits", new AmountRequest(100m, "EUR"), Token);
        await client.PostAsJsonAsync($"/accounts/{account}/withdrawals", new AmountRequest(29.5m, "EUR"), Token);
        await api.Services.GetRequiredService<ProjectionRunner>().CatchUpAsync(Token);

        var balance = await client.GetStringAsync($"/accounts/{account}", Token);
        var statement = await client.GetStringAsync($"/accounts/{account}/statement", Token);

        Assert.Contains("\"balance\":70.50,", balance, StringComparison.Ordinal);
        Assert.Contains("\"amount\":100.00,\"balanceAfter\":100.00,", statement, StringComparison.Ordinal);
        Assert.Contains("\"amount\":-29.50,\"balanceAfter\":70.50,", statement, StringComparison.Ordinal);
    }

    private StoredApi StartApi() => new(connectionString, provider);

    private static async Task<Guid> OpenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest(SomeIban, "EUR"), Token);
        return (await response.Content.ReadFromJsonAsync<AccountOpenedResponse>(Token))!.Id;
    }

    private static async Task<HttpResponseMessage> DepositAsync(HttpClient client, Guid account, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/accounts/{account}/deposits")
        {
            Content = JsonContent.Create(new AmountRequest(50m, "EUR")),
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request, Token);
    }

    // the read model is rebuilt from the events table, so catch it up before reading
    private static async Task<decimal> BalanceAsync(WebApplicationFactory<Program> api, Guid account)
    {
        using var client = api.CreateClient();
        await api.Services.GetRequiredService<ProjectionRunner>().CatchUpAsync(Token);
        return (await client.GetFromJsonAsync<AccountResponse>($"/accounts/{account}", Token))!.Balance;
    }

    private sealed class StoredApi(string connectionString, string provider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
            builder.UseSetting("Storage:Provider", provider);
        }
    }
}
