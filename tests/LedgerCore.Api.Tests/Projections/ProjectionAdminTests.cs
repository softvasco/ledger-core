using System.Net;
using System.Net.Http.Json;
using LedgerCore.Api.Accounts;
using LedgerCore.Application.Projections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Api.Tests.Projections;

public sealed class ProjectionAdminTests : IAsyncDisposable
{
    private const string Summaries = "account-summaries";

    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Admin:Enabled", "true"));

    private readonly HttpClient _client;

    public ProjectionAdminTests() => _client = _factory.CreateClient();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rebuild_answers_202_runs_in_the_background_and_ends_with_the_same_balance()
    {
        var id = await OpenWithDepositAsync(100m);

        var response = await _client.PostAsync($"/admin/projections/{Summaries}/rebuild", null, Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.EndsWith(
            $"/admin/projections/{Summaries}/rebuild", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        var status = await WaitForRebuildAsync();
        Assert.Equal("completed", status.State);
        Assert.Equal(2, status.Applied);
        Assert.NotNull(status.FinishedAt);
        var account = await _client.GetFromJsonAsync<AccountResponse>($"/accounts/{id}", Token);
        Assert.Equal(100m, account!.Balance);
    }

    [Fact]
    public async Task The_list_names_every_projection_and_its_last_rebuild()
    {
        await _client.PostAsync($"/admin/projections/{Summaries}/rebuild", null, Token);
        await WaitForRebuildAsync();

        var projections = await _client.GetFromJsonAsync<List<ProjectionResult>>("/admin/projections", Token);

        var summaries = Assert.Single(projections!);
        Assert.Equal(Summaries, summaries.Name);
        Assert.Equal("completed", summaries.LastRebuild?.State);
    }

    [Fact]
    public async Task An_unknown_projection_is_a_404_problem_with_the_code()
    {
        var response = await _client.PostAsync("/admin/projections/nope/rebuild", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Token);
        Assert.Equal("projection.not_found", problem!.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Asking_for_a_rebuild_that_never_ran_is_a_404()
    {
        var response = await _client.GetAsync($"/admin/projections/{Summaries}/rebuild", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_the_setting_the_admin_routes_do_not_exist()
    {
        await using var plain = new WebApplicationFactory<Program>();

        var response = await plain.CreateClient().PostAsync($"/admin/projections/{Summaries}/rebuild", null, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> OpenWithDepositAsync(decimal amount)
    {
        var opened = await _client.PostAsJsonAsync(
            "/accounts", new OpenAccountRequest("PT50 0002 0123 1234 5678 9015 4", "EUR"), Token);
        var id = (await opened.Content.ReadFromJsonAsync<AccountOpenedResponse>(Token))!.Id;
        (await _client.PostAsJsonAsync($"/accounts/{id}/deposits", new AmountRequest(amount, "EUR"), Token))
            .EnsureSuccessStatusCode();
        await _factory.Services.GetRequiredService<ProjectionRunner>().CatchUpAsync(Token);
        return id;
    }

    private async Task<RebuildResult> WaitForRebuildAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var status = await _client.GetFromJsonAsync<RebuildResult>($"/admin/projections/{Summaries}/rebuild", Token);
            if (status!.State != "running")
            {
                return status;
            }

            await Task.Delay(50, Token);
        }

        throw new TimeoutException("The rebuild was still running after 5 seconds.");
    }

    private sealed record RebuildResult(string Projection, string State, int Applied, long Position, DateTimeOffset? FinishedAt);

    private sealed record ProjectionResult(string Name, RebuildResult? LastRebuild);
}
