using System.Net;
using System.Net.Http.Json;
using LedgerCore.Api.Accounts;
using LedgerCore.Infrastructure.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Api.Tests.Accounts;

public sealed class AccountEndpointTests : IAsyncDisposable
{
    private const string SomeIban = "PT50 0002 0123 1234 5678 9015 4";

    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public AccountEndpointTests() => _client = _factory.CreateClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Opening_an_account_answers_201_with_where_to_read_it()
    {
        var response = await _client.PostAsJsonAsync("/accounts", new OpenAccountRequest(SomeIban, "EUR"), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var opened = await response.Content.ReadFromJsonAsync<AccountOpenedResponse>(Token);
        Assert.EndsWith($"/accounts/{opened!.Id}", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deposits_and_withdrawals_answer_with_the_balance_after_them()
    {
        var id = await OpenAsync();

        var deposited = await PostAmountAsync(id, "deposits", 100m);
        var withdrawn = await PostAmountAsync(id, "withdrawals", 40.5m);

        Assert.Equal(new BalanceResponse(100m, "EUR"), await deposited.Content.ReadFromJsonAsync<BalanceResponse>(Token));
        Assert.Equal(new BalanceResponse(59.5m, "EUR"), await withdrawn.Content.ReadFromJsonAsync<BalanceResponse>(Token));
    }

    [Fact]
    public async Task Overdrawing_is_a_conflict_problem_with_the_error_code()
    {
        var id = await OpenAsync();

        var response = await PostAmountAsync(id, "withdrawals", 10m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(409, problem.Status);
        Assert.Equal("account.insufficient_funds", problem.Extensions["code"]?.ToString());
        Assert.Contains("less than the", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Money_in_another_currency_is_422()
    {
        var id = await OpenAsync();

        var response = await _client.PostAsJsonAsync($"/accounts/{id}/deposits", new AmountRequest(5m, "USD"), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("account.currency_mismatch", (await ReadProblemAsync(response)).Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Money_on_an_unknown_account_is_a_404_problem()
    {
        var response = await PostAmountAsync(Guid.CreateVersion7(), "deposits", 1m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("account.not_found", (await ReadProblemAsync(response)).Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Reading_an_unknown_account_is_a_404_problem()
    {
        var response = await _client.GetAsync($"/accounts/{Guid.CreateVersion7()}", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("account.not_found", (await ReadProblemAsync(response)).Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Every_bad_field_of_a_new_account_is_reported_at_once()
    {
        var response = await _client.PostAsJsonAsync("/accounts", new OpenAccountRequest("PT50 0000", "XYZ"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Token);
        Assert.Equal(["currency", "iban"], problem!.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_amount_finer_than_the_currency_is_400_before_it_reaches_the_ledger()
    {
        var id = await OpenAsync();

        var response = await PostAmountAsync(id, "deposits", 1.005m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Token);
        Assert.Contains("amount", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Reading_an_account_shows_the_read_model_once_it_has_caught_up()
    {
        var id = await OpenAsync();
        await PostAmountAsync(id, "deposits", 25m);

        // the background catch-up runs on a timer; doing it here keeps the test deterministic
        await _factory.Services.GetRequiredService<InMemoryAccountSummaries>().CatchUpAsync(Token);
        var account = await _client.GetFromJsonAsync<AccountResponse>($"/accounts/{id}", Token);

        Assert.Equal(new AccountResponse(id, "PT50000201231234567890154", "EUR", "Open", 25m, 2), account);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> OpenAsync()
    {
        var response = await _client.PostAsJsonAsync("/accounts", new OpenAccountRequest(SomeIban, "EUR"), Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountOpenedResponse>(Token))!.Id;
    }

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(Token))!;
    }

    private Task<HttpResponseMessage> PostAmountAsync(Guid id, string kind, decimal amount) =>
        _client.PostAsJsonAsync($"/accounts/{id}/{kind}", new AmountRequest(amount, "EUR"), Token);
}
