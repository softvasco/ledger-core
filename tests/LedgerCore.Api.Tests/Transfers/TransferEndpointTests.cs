using System.Net;
using System.Net.Http.Json;
using LedgerCore.Api.Accounts;
using LedgerCore.Api.Transfers;
using LedgerCore.Application.Projections;
using LedgerCore.Infrastructure.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Api.Tests.Transfers;

public sealed class TransferEndpointTests : IAsyncDisposable
{
    private const string SomeIban = "PT50 0002 0123 1234 5678 9015 4";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public TransferEndpointTests() => _client = _factory.CreateClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_transfer_answers_201_completed_and_moves_the_money()
    {
        var source = await OpenWithAsync(100m);
        var destination = await OpenWithAsync(0m);

        var response = await _client.PostAsJsonAsync("/transfers", new TransferRequest(source, destination, 40m, "EUR"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = (await response.Content.ReadFromJsonAsync<TransferResponse>(Token))!;
        Assert.Equal(new TransferResponse(transfer.Id, source, destination, 40m, "EUR", "Completed", null), transfer);
        Assert.EndsWith($"/transfers/{transfer.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Equal(60m, await BalanceAsync(source));
        Assert.Equal(40m, await BalanceAsync(destination));
    }

    [Fact]
    public async Task A_refused_transfer_is_the_account_problem_with_the_transfer_id()
    {
        var source = await OpenWithAsync(10m);
        var destination = await OpenWithAsync(0m);

        var response = await _client.PostAsJsonAsync("/transfers", new TransferRequest(source, destination, 40m, "EUR"), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Token))!;
        Assert.Equal("account.insufficient_funds", problem.Extensions["code"]?.ToString());
        var stored = await _client.GetFromJsonAsync<TransferResponse>($"/transfers/{problem.Extensions["transferId"]}", Token);
        Assert.Equal(("Failed", "account.insufficient_funds"), (stored!.Status, stored.Reason));
    }

    [Fact]
    public async Task A_transfer_to_a_missing_account_puts_the_money_back()
    {
        var source = await OpenWithAsync(100m);

        var response = await _client.PostAsJsonAsync(
            "/transfers", new TransferRequest(source, Guid.CreateVersion7(), 40m, "EUR"), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Token))!;
        var stored = await _client.GetFromJsonAsync<TransferResponse>($"/transfers/{problem.Extensions["transferId"]}", Token);
        Assert.Equal("Compensated", stored!.Status);
        Assert.Equal(100m, await BalanceAsync(source));
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_a_400_on_destination()
    {
        var account = await OpenWithAsync(100m);

        var response = await _client.PostAsJsonAsync("/transfers", new TransferRequest(account, account, 40m, "EUR"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Token);
        Assert.Equal(["destination"], problem!.Errors.Keys);
    }

    [Fact]
    public async Task A_retried_transfer_with_the_same_key_moves_the_money_once()
    {
        var source = await OpenWithAsync(100m);
        var destination = await OpenWithAsync(0m);
        var request = new TransferRequest(source, destination, 40m, "EUR");

        var first = await PostWithKeyAsync(request, "transfer-1");
        var retry = await PostWithKeyAsync(request, "transfer-1");

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(retry.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await retry.Content.ReadAsStringAsync(Token));
        Assert.Equal(60m, await BalanceAsync(source));
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("0199a000-0000-7000-8000-000000000001", HttpStatusCode.NotFound)]
    public async Task Reading_a_transfer_that_is_not_there_is_a_problem(string id, HttpStatusCode expected)
    {
        var response = await _client.GetAsync($"/transfers/{id}", Token);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> OpenWithAsync(decimal balance)
    {
        var opened = await _client.PostAsJsonAsync("/accounts", new OpenAccountRequest(SomeIban, "EUR"), Token);
        var id = (await opened.Content.ReadFromJsonAsync<AccountOpenedResponse>(Token))!.Id;
        if (balance > 0)
        {
            (await _client.PostAsJsonAsync($"/accounts/{id}/deposits", new AmountRequest(balance, "EUR"), Token))
                .EnsureSuccessStatusCode();
        }

        return id;
    }

    private async Task<decimal> BalanceAsync(Guid account)
    {
        await _factory.Services.GetRequiredService<ProjectionRunner>().CatchUpAsync(Token);
        return (await _client.GetFromJsonAsync<AccountResponse>($"/accounts/{account}", Token))!.Balance;
    }

    private async Task<HttpResponseMessage> PostWithKeyAsync(TransferRequest request, string key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/transfers") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await _client.SendAsync(message, Token);
    }
}
