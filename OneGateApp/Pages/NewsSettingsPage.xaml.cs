using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.Net.Http.Json;

namespace NeoOrder.OneGate.Pages;

public partial class NewsSettingsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly HttpClient httpClient;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public CategroySetting[]? Categroies { get; set { field = value; OnPropertyChanged(); } }

    public NewsSettingsPage(ApplicationDbContext dbContext, HttpClient httpClient)
    {
        this.LoadingService = new(LoadCategroiesAsync);
        this.dbContext = dbContext;
        this.httpClient = httpClient;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    async Task LoadCategroiesAsync()
    {
        var excluded = await dbContext.Settings.GetAsync<string[]>("news/categories/excluded");
        var categroies = (await httpClient.GetFromJsonAsync<string[]>($"/api/news/categories"))!;
        Categroies = categroies.Select(p => new CategroySetting
        {
            Categroy = p,
            IsEnabled = excluded?.Contains(p) != true
        }).ToArray();
    }

    async void OnSwitchToggled(object sender, ToggledEventArgs e)
    {
        Switch @switch = (Switch)sender;
        CategroySetting? setting = (CategroySetting?)@switch.BindingContext;
        if (setting is null) return;
        var excluded = await dbContext.Settings.GetAsync<HashSet<string>>("news/categories/excluded") ?? [];
        bool changed = e.Value switch
        {
            false => excluded.Add(setting.Categroy),
            true => excluded.Remove(setting.Categroy)
        };
        if (changed)
        {
            await dbContext.Settings.PutAsync("news/categories/excluded", excluded);
            await dbContext.Settings.DeleteAsync("caching/last_update/news");
            GlobalStates.Invalidate<HomePage>();
        }
    }
}
