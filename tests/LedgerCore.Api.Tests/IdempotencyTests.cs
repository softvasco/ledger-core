using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedgerCore.Api.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using LedgerCore.Infrastructure.Accounts;
using LedgerCore.Infrastructure.EventStore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Api.Tests;

public sealed class IdempotencyTests : IAsyncDisposable
{
    private const string SomeIban = "PT50 0002 0123 1234 5678 9015 4";

    private readonly WebApplicationFactory<Program> _factory = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_retried_deposit_with_the_same_key_credits_once()
    {
        using var client = _factory.CreateClient();
        var account = await OpenAsync(client);

        var first = await PostAsync(client, $"/accounts/{account}/deposits", new AmountRequest(50m, "EUR"), "deposit-1");
        var retry = await PostAsync(client, $"/accounts/{account}/deposits", new AmountRequest(50m, "EUR"), "deposit-1");

        Assert.Equal(first.StatusCode, retry.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Token), await retry.Content.ReadAsStringAsync(Token));
        Assert.True(retry.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(50m, await BalanceAsync(client, account));
    }

    [Fact]
    public async Task A_replayed_open_points_to_the_same_account()
    {
        using var client = _factory.CreateClient();
        var request = new OpenAccountRequest(SomeIban, "EUR");

        var first = await PostAsync(client, "/accounts", request, "open-1");
        var retry = await PostAsync(client, "/accounts", request, "open-1");

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.NotNull(retry.Headers.Location);
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
    }

    [Fact]
    public async Task Without_a_key_every_post_runs()
    {
        using var client = _factory.CreateClient();

        Assert.NotEqual(await OpenAsync(client), await OpenAsync(client));
    }

    [Fact]
    public async Task The_same_key_with_another_body_is_a_422()
    {
        using var client = _factory.CreateClient();
        var account = await OpenAsync(client);
        await PostAsync(client, $"/accounts/{account}/deposits", new AmountRequest(50m, "EUR"), "deposit-2");

        var response = await PostAsync(client, $"/accounts/{account}/deposits", new AmountRequest(70m, "EUR"), "deposit-2");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("idempotency.key_reused", await CodeAsync(response));
        Assert.Equal(50m, await BalanceAsync(client, account));
    }

    [Fact]
    public async Task A_key_longer_than_255_characters_is_a_400()
    {
        using var client = _factory.CreateClient();

        var response = await PostAsync(client, "/accounts", new OpenAccountRequest(SomeIban, "EUR"), new string('k', 256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency.invalid_key", await CodeAsync(response));
    }

    // a retry gets the same answer even when the account has changed since
    [Fact]
    public async Task A_refused_command_is_replayed_too()
    {
        using var client = _factory.CreateClient();
        var account = await OpenAsync(client);

        await PostAsync(client, $"/accounts/{account}/withdrawals", new AmountRequest(10m, "EUR"), "withdraw-1");
        await PostAsync(client, $"/accounts/{account}/deposits", new AmountRequest(50m, "EUR"), "deposit-3");
        var retry = await PostAsync(client, $"/accounts/{account}/withdrawals", new AmountRequest(10m, "EUR"), "withdraw-1");

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.True(retry.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(50m, await BalanceAsync(client, account));
    }

    [Fact]
    public async Task A_500_releases_the_key_so_the_retry_runs()
    {
        using var client = _factory
            .WithWebHostBuilder(host => host.ConfigureServices(services =>
                services.AddSingleton<IEventStore>(new FailsOnceEventStore(new InMemoryEventStore()))))
            .CreateClient();
        var request = new OpenAccountRequest(SomeIban, "EUR");

        var failed = await PostAsync(client, "/accounts", request, "open-2");
        var retry = await PostAsync(client, "/accounts", request, "open-2");

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(retry.Headers.Contains("Idempotent-Replayed"));
    }

    // an MCP host calls the endpoint's delegate directly, with no middleware in front of it
    [Fact]
    public async Task A_retry_that_skips_the_middleware_pipeline_still_credits_once()
    {
        using var client = _factory.CreateClient();
        var account = await OpenAsync(client);
        var deposit = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "Deposit");

        var first = await InvokeInProcessAsync(deposit, account, new AmountRequest(50m, "EUR"), "deposit-4");
        var retry = await InvokeInProcessAsync(deposit, account, new AmountRequest(50m, "EUR"), "deposit-4");

        Assert.Equal(StatusCodes.Status200OK, first.Response.StatusCode);
        Assert.Equal("true", retry.Response.Headers["Idempotent-Replayed"]);
        Assert.Equal(50m, await BalanceAsync(client, account));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request, Token);
    }

    private static async Task<Guid> OpenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest(SomeIban, "EUR"), Token);
        return (await response.Content.ReadFromJsonAsync<AccountOpenedResponse>(Token))!.Id;
    }

    // the read model trails the events, so bring it up to date before reading the balance
    private async Task<decimal> BalanceAsync(HttpClient client, Guid account)
    {
        await _factory.Services.GetRequiredService<InMemoryAccountSummaries>().CatchUpAsync(Token);
        return (await client.GetFromJsonAsync<AccountResponse>($"/accounts/{account}", Token))!.Balance;
    }

    private async Task<HttpContext> InvokeInProcessAsync(RouteEndpoint endpoint, Guid account, AmountRequest body, string key)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = Token };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());

        var request = context.Request;
        request.Method = HttpMethods.Post;
        request.Path = $"/accounts/{account}/deposits";
        request.RouteValues["id"] = account.ToString();
        request.Headers["Idempotency-Key"] = key;
        request.ContentType = "application/json";
        request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(body, JsonSerializerOptions.Web));
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);
        return context;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Token))!.Extensions["code"]?.ToString();

    private sealed class FailsOnceEventStore(IEventStore inner) : IEventStore
    {
        private int _appends;

        public Task<long> AppendAsync(
            StreamId stream, long expectedVersion, IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref _appends) == 1
                ? Task.FromException<long>(new InvalidOperationException("database went away"))
                : inner.AppendAsync(stream, expectedVersion, events, cancellationToken);

        public IAsyncEnumerable<RecordedEvent> ReadStreamAsync(
            StreamId stream, long afterVersion = 0, CancellationToken cancellationToken = default) =>
            inner.ReadStreamAsync(stream, afterVersion, cancellationToken);

        public IAsyncEnumerable<RecordedEvent> ReadAllAsync(long afterPosition = 0, CancellationToken cancellationToken = default) =>
            inner.ReadAllAsync(afterPosition, cancellationToken);
    }

    private sealed class HasBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
