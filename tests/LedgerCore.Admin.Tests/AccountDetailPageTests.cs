using Bunit;
using LedgerCore.Admin.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Admin.Tests;

public sealed class AccountDetailPageTests : BunitContext
{
    private static readonly DateTimeOffset Opened = new(2026, 11, 24, 21, 5, 0, TimeSpan.Zero);

    private readonly FakeLedgerApi _api = new();
    private readonly Guid _id = Guid.NewGuid();

    public AccountDetailPageTests() => _api.AddTo(Services);

    [Fact]
    public void An_id_with_no_events_is_reported_as_not_found()
    {
        var page = Render<AccountDetail>(p => p.Add(x => x.Id, _id));

        page.WaitForAssertion(() => Assert.Equal("Account not found", page.Find("h1").TextContent));
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void An_unknown_id_tells_the_router_so_the_response_is_a_404()
    {
        var notFound = 0;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound++;

        var page = Render<AccountDetail>(p => p.Add(x => x.Id, _id));

        page.WaitForAssertion(() => Assert.Equal(1, notFound));
    }

    [Fact]
    public void A_known_id_is_not_reported_to_the_router()
    {
        var notFound = 0;
        Services.GetRequiredService<NavigationManager>().OnNotFound += (_, _) => notFound++;
        _api.Returns($"/accounts/{_id}/history?after=0&pageSize=50", new AccountHistory(
            [new AccountEvent(1, "AccountOpened", Opened, null, null, null)], null));

        var page = Render<AccountDetail>(p => p.Add(x => x.Id, _id));

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.Equal(0, notFound);
    }

    [Fact]
    public void The_timeline_lists_every_event_with_withdrawals_shown_as_negative()
    {
        _api.Returns($"/accounts/{_id}", new AccountRow(_id, "PT50000201231234567890154", "EUR", "Open", 100.25m, 3))
            .Returns($"/accounts/{_id}/history?after=0&pageSize=50", new AccountHistory(
            [
                new AccountEvent(1, "AccountOpened", Opened, null, null, "PT50000201231234567890154"),
                new AccountEvent(2, "MoneyDeposited", Opened.AddMinutes(1), 120.25m, "EUR", null),
                new AccountEvent(3, "MoneyWithdrawn", Opened.AddMinutes(2), 20m, "EUR", null),
            ], null));

        var page = Render<AccountDetail>(p => p.Add(x => x.Id, _id));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("PT50000201231234567890154", page.Find("h1").TextContent);
            Assert.Contains("EUR 100.25", page.Find("dl.facts").TextContent, StringComparison.Ordinal);
            var rows = page.FindAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(c => c.TextContent).ToArray()).ToArray();
            Assert.Equal(["Opened", "Deposit", "Withdrawal"], rows.Select(r => r[2]));
            Assert.Equal(["", "120.25 EUR", "-20 EUR"], rows.Select(r => r[3]));
            Assert.Equal("2026-11-24 21:05:00", rows[0][1]);
        });
    }

    [Fact]
    public void Events_still_show_when_the_read_model_has_not_caught_up()
    {
        _api.Returns($"/accounts/{_id}/history?after=0&pageSize=50", new AccountHistory(
            [new AccountEvent(1, "AccountOpened", Opened, null, null, null)], null));

        var page = Render<AccountDetail>(p => p.Add(x => x.Id, _id));

        page.WaitForAssertion(() =>
        {
            Assert.Equal(_id.ToString(), page.Find("h1").TextContent);
            Assert.Contains("hasn't caught up", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("dl.facts"));
            Assert.Single(page.FindAll("tbody tr"));
        });
    }

    [Fact]
    public void A_later_page_links_back_to_the_start_and_on_to_the_next_cursor()
    {
        _api.Returns($"/accounts/{_id}/history?after=50&pageSize=50", new AccountHistory(
            [new AccountEvent(51, "MoneyDeposited", Opened, 1m, "EUR", null)], 51));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"accounts/{_id}?after=50");

        var page = Render<AccountDetail>(p => p.Add(x => x.Id, _id));

        page.WaitForAssertion(() =>
        {
            var links = page.FindAll("nav.pager a").Select(a => (a.TextContent, a.GetAttribute("href")));
            Assert.Equal([("First events", $"accounts/{_id}"), ("Later events", $"accounts/{_id}?after=51")], links);
        });
    }
}
