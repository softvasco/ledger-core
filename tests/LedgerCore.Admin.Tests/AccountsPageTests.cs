using Bunit;
using LedgerCore.Admin.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace LedgerCore.Admin.Tests;

public sealed class AccountsPageTests : BunitContext
{
    private readonly FakeLedgerApi _api = new();

    public AccountsPageTests() => Services.AddSingleton(_api.Client());

    [Fact]
    public void Each_account_is_a_row_whose_iban_links_to_its_page()
    {
        var id = Guid.NewGuid();
        _api.Returns("/accounts?page=1&pageSize=25",
            new AccountList([new AccountRow(id, "PT50000201231234567890154", "EUR", "Open", 100.25m, 3)], 1, 25, 1));

        var page = Render<Accounts>();

        page.WaitForAssertion(() =>
        {
            var cells = page.FindAll("tbody tr td");
            Assert.Equal(["PT50000201231234567890154", "EUR", "Open", "100.25"], cells.Select(c => c.TextContent.Trim()));
            Assert.Equal($"accounts/{id}", page.Find("tbody a").GetAttribute("href"));
        });
    }

    [Fact]
    public void An_empty_ledger_says_the_read_model_may_not_have_caught_up()
    {
        _api.Returns("/accounts?page=1&pageSize=25", new AccountList([], 1, 25, 0));

        var page = Render<Accounts>();

        page.WaitForAssertion(() => Assert.Contains("No accounts yet", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void The_pager_offers_only_the_pages_that_exist()
    {
        _api.Returns("/accounts?page=2&pageSize=25", new AccountList(Rows(5), 2, 25, 55));
        Services.GetRequiredService<NavigationManager>().NavigateTo("accounts?page=2");

        var page = Render<Accounts>();

        page.WaitForAssertion(() =>
        {
            var links = page.FindAll("nav.pager a").Select(a => (a.TextContent, a.GetAttribute("href")));
            Assert.Equal([("Previous", "accounts?page=1"), ("Next", "accounts?page=3")], links);
            Assert.Contains("Page 2 of 3, 55 accounts", page.Find("nav.pager").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_page_number_below_one_asks_the_api_for_the_first_page()
    {
        _api.Returns("/accounts?page=1&pageSize=25", new AccountList(Rows(1), 1, 25, 1));
        Services.GetRequiredService<NavigationManager>().NavigateTo("accounts?page=0");

        var page = Render<Accounts>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tbody tr")));
        Assert.Equal(["/accounts?page=1&pageSize=25"], _api.Requests);
    }

    private static List<AccountRow> Rows(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new AccountRow(Guid.NewGuid(), $"PT5000020123123456789{i:D4}", "EUR", "Open", 0m, 1))];
}
