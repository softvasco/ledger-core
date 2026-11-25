using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Admin.Tests;

/// <summary>Canned answers for the back office's HTTP calls; anything unknown is a 404.</summary>
internal sealed class FakeLedgerApi : HttpMessageHandler
{
    private readonly Dictionary<string, object> _bodies = new(StringComparer.Ordinal);

    public List<string> Requests { get; } = [];

    public FakeLedgerApi Returns(string pathAndQuery, object body)
    {
        _bodies[pathAndQuery] = body;
        return this;
    }

    // the container creates the HttpClient, so it disposes it with the test context
    public void AddTo(IServiceCollection services) =>
        services
            .AddSingleton(_ => new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("http://ledger.test/") })
            .AddSingleton<LedgerApiClient>();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        Requests.Add(pathAndQuery);
        return Task.FromResult(
            _bodies.TryGetValue(pathAndQuery, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
