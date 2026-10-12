using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using LedgerCore.Api.Accounts;
using LedgerCore.Application.EventStore;
using LedgerCore.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Api.Tests;

public sealed class ProblemResponseTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_the_validators_reject_is_a_400_naming_the_field()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/accounts/{Guid.CreateVersion7()}/deposits", new AmountRequest(0m, "EUR"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ValidationProblemDetails>(response);
        Assert.Equal(["amount"], problem.Errors.Keys);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_a_409_the_client_can_retry()
    {
        using var client = ClientWith(new FailingEventStore(new ConcurrencyConflictException("stream moved")));

        var response = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("PT50 0002 0123 1234 5678 9015 4", "EUR"), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadAsync<ProblemDetails>(response);
        Assert.Equal("concurrency_conflict", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task An_unexpected_failure_is_a_500_that_hides_the_exception()
    {
        using var client = ClientWith(new FailingEventStore(new InvalidOperationException("connection string has a password")));

        var response = await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("PT50 0002 0123 1234 5678 9015 4", "EUR"), Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("password", body, StringComparison.Ordinal);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_malformed_route_value_is_a_400_problem()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/accounts/not-an-id", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(400, (await ReadAsync<ProblemDetails>(response)).Status);
    }

    [Fact]
    public async Task An_unknown_route_is_a_404_problem()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/nowhere", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<ProblemDetails>(response)).Status);
    }

    [Fact]
    public async Task Every_problem_carries_the_trace_id_for_support()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/accounts/{Guid.CreateVersion7()}", Token);

        Assert.True((await ReadAsync<ProblemDetails>(response)).Extensions.ContainsKey("traceId"));
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private HttpClient ClientWith(IEventStore store) =>
        _factory.WithWebHostBuilder(host => host.ConfigureServices(services => services.AddSingleton(store))).CreateClient();

    private sealed class FailingEventStore(Exception failure) : IEventStore
    {
        public Task<long> AppendAsync(
            StreamId stream, long expectedVersion, IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken = default) =>
            Task.FromException<long>(failure);

        public async IAsyncEnumerable<RecordedEvent> ReadStreamAsync(
            StreamId stream, long afterVersion = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<RecordedEvent> ReadAllAsync(
            long afterPosition = 0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
