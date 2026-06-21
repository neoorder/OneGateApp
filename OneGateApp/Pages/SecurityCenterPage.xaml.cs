using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class SecurityCenterPage : ContentPage
{
    const string ShowBalanceKey = "wallet/showBalance";

    readonly ApplicationDbContext dbContext;
    readonly RpcClient rpcClient;
    readonly ConnectedDAppService connectedDAppService;
    bool suppressToggleUpdates;

    public Command RefreshCommand { get; }
    public bool IsRefreshing { get; set { field = value; OnPropertyChanged(); } }
    public bool IsPrivacyModeEnabled { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrivacyModeStatus)); } }
    public bool IsBiometricAvailable { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(BiometricStatus)); OnPropertyChanged(nameof(BiometricStatusText)); OnPropertyChanged(nameof(BiometricActionText)); } }
    public bool IsBiometricEnabled { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(BiometricStatus)); OnPropertyChanged(nameof(BiometricStatusText)); OnPropertyChanged(nameof(BiometricActionText)); } }
    public string RpcStatus { get; set { field = value; OnPropertyChanged(); } } = Strings.Checking;
    public string RpcStatusText { get; set { field = value; OnPropertyChanged(); } } = "";
    public int ConnectedDAppCount { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectedDAppStatus)); OnPropertyChanged(nameof(ConnectedDAppText)); } }
    public string LastCheckedText { get; set { field = value; OnPropertyChanged(); } } = "";
    public string PrivacyModeStatus => IsPrivacyModeEnabled ? Strings.Enabled : Strings.Disabled;
    public string BiometricStatus => IsBiometricEnabled ? Strings.Enabled : IsBiometricAvailable ? Strings.Available : Strings.Unavailable;
    public string BiometricStatusText => IsBiometricEnabled
        ? Strings.BiometricEnabledText
        : IsBiometricAvailable
            ? Strings.BiometricAvailableText
            : Strings.BiometricUnavailableSecurityText;
    public string BiometricActionText => IsBiometricEnabled ? Strings.DisableBiometric : Strings.CreateBiometricCredential;
    public string ConnectedDAppStatus => ConnectedDAppCount == 0
        ? Strings.ConnectedDAppsNoneStatus
        : string.Format(Strings.ConnectedDAppsCountStatus, ConnectedDAppCount);
    public string ConnectedDAppText => ConnectedDAppCount == 0
        ? Strings.ConnectedDAppsSecurityTextEmpty
        : Strings.ConnectedDAppsSecurityTextConnected;

    public SecurityCenterPage(ApplicationDbContext dbContext, RpcClient rpcClient, ConnectedDAppService connectedDAppService)
    {
        this.dbContext = dbContext;
        this.rpcClient = rpcClient;
        this.connectedDAppService = connectedDAppService;
        RefreshCommand = new Command(async () => await RefreshAsync());
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshCommand.Execute(null);
    }

    async Task RefreshAsync()
    {
        suppressToggleUpdates = true;
        try
        {
            IsPrivacyModeEnabled = !await dbContext.Settings.GetAsync<bool>(ShowBalanceKey);
            IsBiometricAvailable = await DataProtectionService.CheckAvailabilityAsync();
            IsBiometricEnabled = await dbContext.Settings.ExistsAsync("biometric/credential");
            ConnectedDAppCount = (await connectedDAppService.LoadAsync()).Length;
            await RefreshRpcStatusAsync();
        }
        finally
        {
            LastCheckedText = string.Format(Strings.LastCheckedAt, DateTimeOffset.Now.ToString("g", CultureInfo.CurrentCulture));
            suppressToggleUpdates = false;
            IsRefreshing = false;
        }
    }

    async Task RefreshRpcStatusAsync()
    {
        try
        {
            uint blockCount = await rpcClient.GetBlockCount().WaitAsync(TimeSpan.FromSeconds(8));
            RpcStatus = Strings.Connected;
            RpcStatusText = string.Format(Strings.RpcStatusConnectedText, SharedOptions.RpcServerUri.Host, blockCount);
        }
        catch
        {
            RpcStatus = Strings.ConnectionIssue;
            RpcStatusText = string.Format(Strings.RpcStatusUnavailableText, SharedOptions.RpcServerUri.Host);
        }
    }

    async void OnPrivacyModeToggled(object sender, ToggledEventArgs e)
    {
        if (suppressToggleUpdates) return;
        IsPrivacyModeEnabled = e.Value;
        try
        {
            await dbContext.Settings.PutAsync(ShowBalanceKey, !e.Value);
            GlobalStates.Invalidate<WalletPage>();
            GlobalStates.Invalidate<AssetDetailsPage>();
        }
        catch (Exception ex)
        {
            RevertToggleState(() => IsPrivacyModeEnabled = !e.Value);
            await Toast.Show(ex.Message);
        }
    }

    void RevertToggleState(Action revert)
    {
        suppressToggleUpdates = true;
        try
        {
            revert();
        }
        finally
        {
            suppressToggleUpdates = false;
        }
    }

    async void OnReviewBackupTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//wallet/details/export");
    }

    async void OnManagePasswordTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//wallet/details/password");
    }

    async void OnManageBiometricsTapped(object sender, TappedEventArgs e)
    {
        if (!IsBiometricAvailable)
        {
            await Toast.Show(Strings.BiometricUnavailableText);
            return;
        }
        string route = IsBiometricEnabled ? "//home/settings/biometric/disable" : "//home/settings/biometric/create";
        await Shell.Current.GoToAsync(route);
    }

    async void OnConnectedAppsTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//home/settings/security/connected-apps");
    }
}
