using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.Net;

namespace NeoOrder.OneGate.Pages;

public partial class HomePage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly IDispatcherTimer timer;

    public LoadingService LoadingService { get; }
    public CachedCollection<Banner> Banners { get; }
    public CachedCollection<News> News { get; }

    public HomePage(IServiceProvider serviceProvider, ApplicationDbContext dbContext)
    {
        this.LoadingService = new(LoadBannersAsync, LoadNewsAsync);
        this.dbContext = dbContext;
        this.Banners = serviceProvider.GetServiceOrCreateInstance<CachedCollection<Banner>>();
        this.News = serviceProvider.GetServiceOrCreateInstance<CachedCollection<News>>();
        this.timer = Dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromSeconds(5);
        this.timer.Tick += Timer_Tick;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadingService.BeginLoad();
        CheckForUpdates();
        timer.Start();
    }

    protected override void OnDisappearing()
    {
        timer.Stop();
        base.OnDisappearing();
    }

    async void CheckForUpdates()
    {
        var last_check = await dbContext.Settings.GetAsync<DateTimeOffset>("system/last_update_check");
        if (last_check > DateTimeOffset.UtcNow - TimeSpan.FromDays(7)) return;
        await dbContext.Settings.PutAsync("system/last_update_check", DateTimeOffset.UtcNow);
        await Commands.CheckForUpdates.ExecuteAsync(true);
    }

    void Timer_Tick(object? sender, EventArgs e)
    {
        // There are some issues with CarouselView on Windows, so we disable auto-scrolling on that platform for now
#if !WINDOWS
        if (Banners.Count > 1)
        {
            carouselView.Position = (carouselView.Position + 1) % Banners.Count;
        }
#endif
    }

    async void OnBannerTapped(object sender, TappedEventArgs e)
    {
        string url = (string)e.Parameter!;
        Uri uri = new(url);
        if (uri.Scheme == "https" && uri.Authority == SharedOptions.OneGateDomain && uri.Segments is ["/", "app/", _])
            await Commands.LaunchDApp.ExecuteAsync(uri);
        else
            await Commands.OpenUrl.ExecuteAsync(url);
    }

    async void OnNewsTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//home/news/details", new Dictionary<string, object>
        {
            ["news"] = e.Parameter!
        });
    }

    async Task LoadBannersAsync()
    {
        await Banners.LoadAsync("/api/banners", TimeSpan.FromDays(1));
    }

    async Task LoadNewsAsync()
    {
        string url = "/api/news";
        string[]? excluded = await dbContext.Settings.GetAsync<string[]>("news/categories/excluded");
        if (excluded?.Length > 0)
            url += "?" + string.Join('&', excluded.Select(p => "x=" + WebUtility.UrlEncode(p)));
        await News.LoadAsync(url, TimeSpan.FromMinutes(15));
    }
}
