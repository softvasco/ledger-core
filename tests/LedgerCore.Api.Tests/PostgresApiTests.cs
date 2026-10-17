using System.Net;
using System.Net.Http.Json;
using LedgerCore.Api.Accounts;
using LedgerCore.Infrastructure.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LedgerCore.Api.Tests;

public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

public sealed class PostgresApiTests(PostgresContainer database) : IClassFixture<PostgresContainer>
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

    private PostgresApi StartApi() => new(database.ConnectionString);

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
        await api.Services.GetRequiredService<InMemoryAccountSummaries>().CatchUpAsync(Token);
        return (await client.GetFromJsonAsync<AccountResponse>($"/accounts/{account}", Token))!.Balance;
    }

    private sealed class PostgresApi(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:Ledger", connectionString);
    }
}
