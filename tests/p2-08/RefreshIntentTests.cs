using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Pages;
using System.Windows.Input;
using Xunit;

public class RefreshIntentTests
{
    [Fact]
    public void GamesManualRefreshBypassesCacheWhileAutomaticLoadKeepsIt()
    {
        var games = new CachedCollection<DApp>();
        var page = new GamingPage(new TestServices(games), new());
        Assert.Equal([false], games.ForcedRequests);

        ((ICommand)page.LoadingService).Execute(null);

        Assert.Equal([false, true], games.ForcedRequests);
        Assert.False(page.LoadingService.IsReloading);
    }

    [Fact]
    public void HomeRefreshBypassesBothBannerAndNewsCache()
    {
        var banners = new CachedCollection<Banner>();
        var news = new CachedCollection<News>();
        var page = new HomePage(new TestServices(banners, news), new());
        page.LoadingService.BeginLoad();
        Assert.Equal([false], banners.ForcedRequests);
        Assert.Equal([false], news.ForcedRequests);

        ((ICommand)page.LoadingService).Execute(null);

        Assert.Equal([false, true], banners.ForcedRequests);
        Assert.Equal([false, true], news.ForcedRequests);
    }
}
