using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Pages;

public partial class AuthenticatePage : ContentPage, IQueryAttributable
{
    readonly ProtocolSettings protocolSettings;
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly ConnectedDAppService connectedDAppService;
    readonly Wallet wallet;
    readonly HttpClient httpClient;

    public string? DAppIdentifier { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(CallbackText)); } }
    public AuthenticationChallengePayload? Payload { get; private set { field = value; OnPropertyChanged(); RefreshRequestState(); } }
    public bool IsProcessing { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanAuthorize)); } }
    public bool IsIdle => !IsProcessing;
    public bool CanAuthorize => !IsProcessing && ErrorMessage is null && Payload is not null;
    public string? ErrorMessage { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAuthorize)); } }
    public string? WalletAddress => wallet.GetDefaultAccount()?.Address;
    public string NetworkText => FormatNetwork(protocolSettings.Network);
    public string CallbackText => DAppIdentifier is null ? Payload?.Callback?.ToString() ?? "--" : $"dapp://{DAppIdentifier}/auth";

    public AuthenticatePage(ProtocolSettings protocolSettings, WalletAuthorizationService walletAuthorizationService, ConnectedDAppService connectedDAppService, IWalletProvider walletProvider, HttpClient httpClient)
    {
        this.protocolSettings = protocolSettings;
        this.walletAuthorizationService = walletAuthorizationService;
        this.connectedDAppService = connectedDAppService;
        this.wallet = walletProvider.GetWallet()!;
        this.httpClient = httpClient;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("dapp", out var obj_dapp))
            DAppIdentifier = (string)obj_dapp;
        Payload = (AuthenticationChallengePayload)query["payload"];
    }

    async void OnAuthorizeClicked(object sender, EventArgs e)
    {
        if (Payload is null || IsProcessing || ErrorMessage is not null) return;
        IsProcessing = true;
        try
        {
            await Authenticate(Payload);
            await this.GoBackOrCloseAsync();
        }
        catch (OperationCanceledException ex)
        {
            await NotifyAuthenticationErrorAsync(ex, showToast: false);
            await this.GoBackOrCloseAsync();
        }
        catch (Exception ex)
        {
            await NotifyAuthenticationErrorAsync(ex, showToast: true);
            await this.GoBackOrCloseAsync();
        }
        finally
        {
            IsProcessing = false;
        }
    }

    async void OnCancelClicked(object sender, EventArgs e)
    {
        if (IsProcessing) return;
        await NotifyAuthenticationErrorAsync(new OperationCanceledException(Strings.OperationCancelled), showToast: false);
        await this.GoBackOrCloseAsync();
    }

    async Task Authenticate(AuthenticationChallengePayload payload)
    {
        AuthenticationResponsePayload response = await AuthenticateAsync(payload);
        if (DAppIdentifier is null)
        {
            ValidateCallback(payload);
            var message = await httpClient.PostAsJsonAsync(payload.Callback, response, SharedOptions.JsonSerializerOptions);
            message.EnsureSuccessStatusCode();
        }
        else
        {
            string result = WebUtility.UrlEncode(JsonSerializer.Serialize(response, SharedOptions.JsonSerializerOptions));
            string uri = $"dapp://{DAppIdentifier}/auth?result={result}";
            if (!await Launcher.TryOpenAsync(uri))
                await Toast.Show(Strings.OpenDAppFailedText);
        }
    }

    async Task<AuthenticationResponsePayload> AuthenticateAsync(AuthenticationChallengePayload payload)
    {
        payload.Validate(protocolSettings);
        if (!await walletAuthorizationService.RequestAuthorizationAsync(this, Strings.LoginRequest, Strings.LoginRequestText, payload.Domain))
            throw new OperationCanceledException();
        await connectedDAppService.ConnectAsync(payload.Domain, DAppIdentifier);
        WalletAccount account = wallet.GetDefaultAccount()!;
        return payload.CreateResponse(account, protocolSettings);
    }

    async Task NotifyAuthenticationErrorAsync(Exception ex, bool showToast)
    {
        string message = ex is OperationCanceledException ? Strings.OperationCancelled : ex.Message;
        if (DAppIdentifier is null)
        {
            if (showToast)
                await Toast.Show(message);
            return;
        }

        var error = new JsonObject
        {
            ["code"] = ex.HResult,
            ["message"] = message
        };
        string text = WebUtility.UrlEncode(JsonSerializer.Serialize(error, SharedOptions.JsonSerializerOptions));
        string uri = $"dapp://{DAppIdentifier}/auth?error={text}";
        if (!await Launcher.TryOpenAsync(uri) && showToast)
            await Toast.Show(message);
    }

    void RefreshRequestState()
    {
        try
        {
            if (Payload is not null)
            {
                Payload.Validate(protocolSettings);
                if (DAppIdentifier is null)
                    ValidateCallback(Payload);
            }
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        OnPropertyChanged(nameof(CallbackText));
    }

    static void ValidateCallback(AuthenticationChallengePayload payload)
    {
        if (payload.Callback is null || !payload.Callback.IsAbsoluteUri || payload.Callback.Scheme != "https" || payload.Callback.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.InvalidCallbackURLFormat);
    }

    string FormatNetwork(uint network)
    {
        return network == protocolSettings.Network
            ? $"Neo N3 ({network})"
            : network.ToString();
    }
}
