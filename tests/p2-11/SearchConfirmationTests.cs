using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using Xunit;

public class SearchConfirmationTests
{
    [Theory]
    [InlineData("Beta", "Beta", 1)]
    [InlineData("Missing", null, 0)]
    [InlineData("", null, 0)]
    [InlineData("a", null, 2)]
    public async Task ConfirmationUsesCurrentTextBeforePendingDebounce(string query, string? expected, int resultCount)
    {
        Commands.LaunchDApp.Last = null;
        var catalog = new CachedCollection<DApp> { new() { Name = "Alpha" }, new() { Name = "Beta" } };
        var page = new GlobalSearchPage(new TestServices(catalog), new(), new());
        await page.LoadForTestAsync();
        page.ChangeQueryForTest("Alpha");
        page.Dispatcher.Flush();
        Assert.Single(page.Results);

        page.ChangeQueryForTest(query);
        page.ConfirmForTest();

        Assert.Equal(expected, Commands.LaunchDApp.Last?.Name);
        Assert.Equal(resultCount, page.Results.Length);
        page.Dispatcher.Flush();
        Assert.Equal(expected, Commands.LaunchDApp.Last?.Name);
    }
}
