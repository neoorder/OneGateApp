using CommunityToolkit.Maui.Alerts;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class SecurityCenterPage : ContentPage
{
    const string BackupConfirmedKey = "wallet/backup_confirmed";
    const string ShowBalanceKey = "wallet/showBalance";

    readonly ApplicationDbContext dbContext;
    readonly RpcClient rpcClient;
    bool suppressToggleUpdates;

    public Command RefreshCommand { get; }
    public Wallet Wallet { get; }
    public bool IsRefreshing { get; set { field = value; OnPropertyChanged(); } }
    public bool IsBackupConfirmed { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackupStatus)); OnPropertyChanged(nameof(BackupStatusText)); } }
    public bool IsPrivacyModeEnabled { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(PrivacyModeStatus)); } }
    public bool IsBiometricAvailable { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(BiometricStatus)); OnPropertyChanged(nameof(BiometricStatusText)); OnPropertyChanged(nameof(BiometricActionText)); } }
    public bool IsBiometricEnabled { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(BiometricStatus)); OnPropertyChanged(nameof(BiometricStatusText)); OnPropertyChanged(nameof(BiometricActionText)); } }
    public string RpcStatus { get; set { field = value; OnPropertyChanged(); } } = Strings.Checking;
    public string RpcStatusText { get; set { field = value; OnPropertyChanged(); } } = "";
    public string LastCheckedText { get; set { field = value; OnPropertyChanged(); } } = "";

    public string BackupStatus => IsBackupConfirmed ? Strings.BackupConfirmed : Strings.BackupNotConfirmed;
    public string BackupStatusText => IsBackupConfirmed ? Strings.BackupConfirmedText : Strings.BackupNotConfirmedText;
    public string PrivacyModeStatus => IsPrivacyModeEnabled ? Strings.Enabled : Strings.Disabled;
    public string BiometricStatus => IsBiometricEnabled ? Strings.Enabled : IsBiometricAvailable ? Strings.Available : Strings.Unavailable;
    public string BiometricStatusText => IsBiometricEnabled
        ? Strings.BiometricEnabledText
        : IsBiometricAvailable
            ? Strings.BiometricAvailableText
            : Strings.BiometricUnavailableSecurityText;
    public string BiometricActionText => IsBiometricEnabled ? Strings.DisableBiometric : Strings.CreateBiometricCredential;

    public SecurityCenterPage(ApplicationDbContext dbContext, IWalletProvider walletProvider, RpcClient rpcClient)
    {
        this.dbContext = dbContext;
        this.rpcClient = rpcClient;
        Wallet = walletProvider.GetWallet()!;
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
        if (IsRefreshing) return;
        IsRefreshing = true;
        suppressToggleUpdates = true;
        try
        {
            IsBackupConfirmed = await dbContext.Settings.GetAsync<bool>(BackupConfirmedKey);
            IsPrivacyModeEnabled = !await dbContext.Settings.GetAsync<bool>(ShowBalanceKey);
            IsBiometricAvailable = await DataProtectionService.CheckAvailabilityAsync();
            IsBiometricEnabled = await dbContext.Settings.ExistsAsync("biometric/credential");
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

    async void OnBackupConfirmedToggled(object sender, ToggledEventArgs e)
    {
        if (suppressToggleUpdates) return;
        IsBackupConfirmed = e.Value;
        await dbContext.Settings.PutAsync(BackupConfirmedKey, e.Value);
    }

    async void OnPrivacyModeToggled(object sender, ToggledEventArgs e)
    {
        if (suppressToggleUpdates) return;
        IsPrivacyModeEnabled = e.Value;
        await dbContext.Settings.PutAsync(ShowBalanceKey, !e.Value);
        GlobalStates.Invalidate<WalletPage>();
        GlobalStates.Invalidate<AssetDetailsPage>();
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

    async void OnReviewDAppsTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//dapps");
    }
}
