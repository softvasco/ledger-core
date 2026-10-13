using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LedgerCore.Api.Tests;

public sealed class OpenApiDocumentTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_document_is_served_as_openapi_3_1()
    {
        using var document = await ReadDocumentAsync();

        Assert.StartsWith("3.1", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("ledger-core API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("/accounts", "post", new[] { "201", "400", "409" })]
    [InlineData("/accounts/{id}", "get", new[] { "200", "400", "404" })]
    [InlineData("/accounts/{id}/deposits", "post", new[] { "200", "400", "404", "409", "422" })]
    [InlineData("/accounts/{id}/withdrawals", "post", new[] { "200", "400", "404", "409", "422" })]
    public async Task Every_status_an_endpoint_can_answer_is_listed(string path, string method, string[] statuses)
    {
        using var document = await ReadDocumentAsync();

        var responses = Operation(document, path, method).GetProperty("responses");

        Assert.Equal(statuses, responses.EnumerateObject().Select(r => r.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("/accounts/{id}/withdrawals", "post", "409")]
    [InlineData("/accounts/{id}/deposits", "post", "422")]
    [InlineData("/accounts/{id}", "get", "404")]
    public async Task Errors_are_documented_as_problem_json(string path, string method, string status)
    {
        using var document = await ReadDocumentAsync();

        var content = Operation(document, path, method).GetProperty("responses").GetProperty(status).GetProperty("content");

        Assert.True(content.TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task Every_operation_has_a_summary_and_an_operation_id()
    {
        using var document = await ReadDocumentAsync();

        var operations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .ToList();

        Assert.NotEmpty(operations);
        Assert.All(operations, op =>
        {
            Assert.False(string.IsNullOrWhiteSpace(op.Value.GetProperty("summary").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(op.Value.GetProperty("operationId").GetString()));
        });
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<JsonDocument> ReadDocumentAsync()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
    }

    private static JsonElement Operation(JsonDocument document, string path, string method) =>
        document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
}
