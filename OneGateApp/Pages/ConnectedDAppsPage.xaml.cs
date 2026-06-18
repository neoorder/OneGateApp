using CommunityToolkit.Maui.Alerts;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class ConnectedDAppsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;

    public ObservableCollection<ConnectedDAppPermissionItem> Grants { get; } = [];
    public bool HasGrants => Grants.Count > 0;
    public bool HasNoGrants => !HasGrants;

    public ConnectedDAppsPage(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        Grants.Clear();
        Setting[] settings = await dbContext.Settings
            .Where(p => p.Key.StartsWith("dapps/permissions/"))
            .ToArrayAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (Setting setting in settings)
        {
            DAppPermissionGrant? grant = await dbContext.Settings.GetAsync<DAppPermissionGrant>(setting.Key);
            if (DAppPermissions.IsFresh(grant, now))
                Grants.Add(new ConnectedDAppPermissionItem(
                    grant!.Host,
                    string.Format(Strings.LastUsedFormat, grant.LastUsedAt.LocalDateTime)));
        }
        OnPropertyChanged(nameof(HasGrants));
        OnPropertyChanged(nameof(HasNoGrants));
    }

    async void OnDisconnectClicked(object sender, EventArgs e)
    {
        if (((Button)sender).CommandParameter is not string host) return;
        await dbContext.Settings.DeleteAsync(DAppPermissions.SettingsKeyForHost(host));
        ConnectedDAppPermissionItem? grant = Grants.FirstOrDefault(p => p.Host == host);
        if (grant is not null)
            Grants.Remove(grant);
        OnPropertyChanged(nameof(HasGrants));
        OnPropertyChanged(nameof(HasNoGrants));
        await Toast.Make(Strings.DAppDisconnected).Show();
    }
}

public sealed record ConnectedDAppPermissionItem(string Host, string DisplayLastUsedAt);
