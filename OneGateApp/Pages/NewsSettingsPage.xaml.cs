using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.Net.Http.Json;

namespace NeoOrder.OneGate.Pages;

public partial class NewsSettingsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly IDbContextFactory<CacheDbContext> cacheDbContextFactory;
    readonly HttpClient httpClient;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public CategroySetting[]? Categroies { get; set { field = value; OnPropertyChanged(); } }

    public NewsSettingsPage(ApplicationDbContext dbContext, IDbContextFactory<CacheDbContext> cacheDbContextFactory, HttpClient httpClient)
    {
        this.LoadingService = new(LoadCategroiesAsync);
        this.dbContext = dbContext;
        this.cacheDbContextFactory = cacheDbContextFactory;
        this.httpClient = httpClient;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    async Task LoadCategroiesAsync()
    {
        var excluded = await dbContext.ExcludedNewsCategories.Select(p => p.Category).ToArrayAsync();
        var categroies = (await httpClient.GetFromJsonAsync<string[]>($"/api/news/categories"))!;
        Categroies = categroies.Select(p => new CategroySetting
        {
            Categroy = p,
            IsEnabled = !excluded.Contains(p)
        }).ToArray();
    }

    async void OnSwitchToggled(object sender, ToggledEventArgs e)
    {
        Switch @switch = (Switch)sender;
        CategroySetting? setting = (CategroySetting?)@switch.BindingContext;
        if (setting is null) return;
        int changed = e.Value
            ? await dbContext.ExcludedNewsCategories.Where(p => p.Category == setting.Categroy).ExecuteDeleteAsync()
            : await dbContext.Database.ExecuteSqlAsync($"""
                INSERT INTO [ExcludedNewsCategories]([Category]) VALUES({setting.Categroy})
                ON CONFLICT([Category]) DO NOTHING
                """);
        if (changed > 0)
        {
            await CachedCollection<News>.InvalidateAsync(cacheDbContextFactory);
            GlobalStates.Invalidate<HomePage>();
        }
    }
}
