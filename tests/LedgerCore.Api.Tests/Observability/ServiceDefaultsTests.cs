using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using LedgerCore.Api.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace LedgerCore.Api.Tests.Observability;

public sealed class ServiceDefaultsTests : IAsyncDisposable
{
    private readonly List<Activity> _spans = [];
    private readonly WebApplicationFactory<Program> _root = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ServiceDefaultsTests() =>
        _factory = _root.WithWebHostBuilder(web =>
            web.ConfigureServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(_spans))));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_answers_healthy_without_touching_storage()
    {
        var response = await _factory.CreateClient().GetAsync("/alive", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task A_request_is_traced_with_its_route_template()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("PT50 0002 0123 1234 5678 9015 4", "EUR"), Token);
        Flush();

        Assert.Contains(_spans, span => span.Kind == ActivityKind.Server && Equals(span.GetTagItem("http.route"), "/accounts/"));
    }

    [Fact]
    public async Task Ledger_spans_are_collected_next_to_the_http_ones()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/accounts", new OpenAccountRequest("PT50 0002 0123 1234 5678 9015 4", "EUR"), Token);
        Flush();

        Assert.Contains(_spans, span => span.Source.Name == "LedgerCore.Application");
    }

    [Fact]
    public async Task Health_checks_are_not_traced()
    {
        await _factory.CreateClient().GetAsync("/health", Token);
        Flush();

        Assert.DoesNotContain(_spans, span => Equals(span.GetTagItem("url.path"), "/health"));
    }

    // the in-memory exporter sits behind a batch processor, so force it out before asserting
    private void Flush() => _factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

    // disposing the root also disposes the factory derived from it
    public ValueTask DisposeAsync() => _root.DisposeAsync();
}
