using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RemoteDebug;
using NeoOrder.OneGate.DebugProtocol;
using SkiaSharp;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZXing.Common;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace NeoOrder.OneGate.Pages;

public partial class ScanPage : ContentPage, IQueryAttributable
{
    readonly ProtocolSettings protocolSettings;
    readonly RemoteDebugService remoteDebugService;
    readonly CameraBarcodeReaderView? cameraView;
    string? action;
    bool isPageVisible;
    bool isProcessingScan;

    public ScanPage(ProtocolSettings protocolSettings, RemoteDebugService remoteDebugService)
    {
        this.protocolSettings = protocolSettings;
        this.remoteDebugService = remoteDebugService;
        InitializeComponent();
        // Photo recognition must remain available on platforms without a camera scanning handler.
        if (BarcodeScanning.IsSupported)
        {
            cameraView = new CameraBarcodeReaderView();
            cameraView.Options = cameraView.Options with { Formats = BarcodeFormat.QrCode };
            cameraView.BarcodesDetected += OnBarcodesDetected;
            cameraHost.Content = cameraView;
            cameraControls.BindingContext = cameraView;
            cameraControls.IsVisible = true;
        }
        photoOnlyButton.IsVisible = cameraView is null;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("action", out var action))
            this.action = (string)action;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        if (cameraView is not null) cameraView.IsDetecting = !isProcessingScan;
    }

    protected override void OnDisappearing()
    {
        isPageVisible = false;
        if (cameraView is not null) cameraView.IsDetecting = false;
        base.OnDisappearing();
    }

    bool IsCurrentScanPage => isPageVisible && ReferenceEquals(Shell.Current?.CurrentPage, this);

    async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        string? value = e.Results.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;
        // Camera callbacks can arrive on a worker thread or repeat while navigation is pending.
        await MainThread.InvokeOnMainThreadAsync(() => ProcessScanAsync(() => Task.FromResult<string?>(value)));
    }

    async void OnPickPhotoClicked(object sender, EventArgs e) => await ProcessScanAsync(ReadPhotoCodeAsync);

    async Task<string?> ReadPhotoCodeAsync()
    {
        FileResult? photo;
        if (cameraView is null)
        {
            photo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = Strings.ScanFromPhotoLibrary,
                FileTypes = FilePickerFileType.Images
            });
        }
        else
        {
            var results = await MediaPicker.PickPhotosAsync(new MediaPickerOptions { Title = Strings.ScanFromPhotoLibrary });
            photo = results.FirstOrDefault();
        }
        if (photo is null || !IsCurrentScanPage) return null;
        await using var stream = await photo.OpenReadAsync();
        if (!IsCurrentScanPage) return null;
        // Image decoding and QR recognition can be expensive; keep both off the UI thread.
        string? result = await Task.Run(() => DecodePhotoCode(stream));
        if (!IsCurrentScanPage) return null;
        if (string.IsNullOrWhiteSpace(result))
        {
            await Toast.Show(Strings.ErrorMessageUnableReadQRCode);
            return null;
        }
        return result;
    }

    static string? DecodePhotoCode(Stream stream)
    {
        using var managedStream = new SKManagedStream(stream);
        using var bitmap = SKBitmap.Decode(managedStream);
        if (bitmap is null) return null;
        var reader = new ZXing.SkiaSharp.BarcodeReader
        {
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [ZXing.BarcodeFormat.QR_CODE]
            },
            AutoRotate = true
        };
        return reader.Decode(bitmap)?.Text;
    }

    async Task ProcessScanAsync(Func<Task<string?>> readCode)
    {
        if (isProcessingScan || !IsCurrentScanPage) return;
        isProcessingScan = true;
        if (cameraView is not null) cameraView.IsDetecting = false;
        try
        {
            string? result = await readCode();
            if (IsCurrentScanPage && !string.IsNullOrWhiteSpace(result))
                await ProcessScanResultAsync(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentScanPage) await Toast.Show(ex.Message);
        }
        finally
        {
            isProcessingScan = false;
            if (cameraView is not null) cameraView.IsDetecting = IsCurrentScanPage;
        }
    }

    void OnTorchClicked(object sender, EventArgs e)
    {
        if (cameraView is not null) cameraView.IsTorchOn = !cameraView.IsTorchOn;
    }

    async Task ProcessScanResultAsync(string result)
    {
        result = result.Trim();
        try
        {
            if (Uri.TryCreate(result, UriKind.Absolute, out var uri))
            {
                if (await ProcessUriAsync(uri)) return;
            }
            else if (result.StartsWith('{'))
            {
                JsonObject? json = JsonSerializer.Deserialize<JsonObject>(result, SharedOptions.JsonSerializerOptions);
                if (json is not null && await ProcessJsonAsync(json, result)) return;
            }
            else
            {
                if (await ProcessTextAsync(result)) return;
            }
        }
        catch
        {
        }
        if (IsCurrentScanPage)
        {
            string errorMessage = string.Format(Strings.ErrorMessageUnsupportedQRCode, result);
            await DisplayAlertAsync(null, errorMessage, Strings.OK);
        }
    }

    async Task<bool> ProcessUriAsync(Uri uri)
    {
        return uri.Scheme switch
        {
            "neo" => await ProcessNeoSchemeAsync(uri),
            "https" => await ProcessHttpsSchemeAsync(uri),
            PairingInvitation.Scheme => await ProcessRemoteDebugPairingAsync(uri),
            _ => false,
        };
    }

    async Task<bool> ProcessRemoteDebugPairingAsync(Uri uri)
    {
        if (action != "PairRemoteDebug") return false;
        PairingInvitation invitation = PairingInvitation.Parse(uri.AbsoluteUri);
        bool confirmed = await DisplayAlertAsync(
            Strings.RemoteDebugPairingTitle,
            string.Format(Strings.RemoteDebugPairingPrompt, invitation.DebuggerName ?? "Remote debugger", Convert.ToHexString(invitation.DebuggerPublicKey)[..16]),
            Strings.Authorize,
            Strings.Cancel);
        if (!confirmed) return false;
        await remoteDebugService.PairAsync(invitation);
        await Shell.Current.GoToAsync("..");
        return true;
    }

    async Task<bool> ProcessNeoSchemeAsync(Uri uri)
    {
        PaymentAction? payment = PaymentAction.TryCreate(uri);
        if (payment is null) return false;
        try
        {
            payment.Recipient.ToScriptHash(protocolSettings.AddressVersion);
        }
        catch
        {
            return false;
        }
        string query = "?address=" + uri.LocalPath;
        if (payment.AssetId is not null)
            query += $"&asset={payment.AssetId}";
        if (payment.Amount.HasValue)
            query += $"&amount={payment.Amount}";
        switch (action)
        {
            case "AddContact":
                await Shell.Current.GoToAsync("..", new ShellNavigationQueryParameters { ["input"] = payment.Recipient });
                return true;
            case "RecognizeAddress":
                await Shell.Current.GoToAsync($".." + query);
                return true;
            case null:
                await Shell.Current.GoToAsync("..");
                await Shell.Current.GoToAsync($"//wallet/send" + query);
                return true;
            default:
                return false;
        }
    }

    async Task<bool> ProcessHttpsSchemeAsync(Uri uri)
    {
        switch (AppLinkAction.TryCreate(uri))
        {
            case LaunchDAppAction link:
                if (await ProcessDappUriAsync(link)) return true;
                break;
            case ViewNewsAction link:
                if (await ProcessNewsUriAsync(link)) return true;
                break;
        }
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await Browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
            return true;
        }
        return false;
    }

    async Task<bool> ProcessDappUriAsync(LaunchDAppAction link)
    {
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await Commands.LaunchDApp.ExecuteAsync(link.Uri);
            return true;
        }
        return false;
    }

    async Task<bool> ProcessNewsUriAsync(ViewNewsAction link)
    {
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await link.GotoRoute(Shell.Current);
            return true;
        }
        return false;
    }

    async Task<bool> ProcessJsonAsync(JsonObject json, string raw)
    {
        if ((action is null or "AddContact") && json["$type"]?.GetValue<string>() == "meshline.contact.invite")
        {
            var parameters = new ShellNavigationQueryParameters { ["input"] = raw };
            if (action == "AddContact")
                await Shell.Current.GoToAsync("..", parameters);
            else
            {
                await Shell.Current.GoToAsync("..");
                await Shell.Current.GoToAsync("//home/add-contact", parameters);
            }
            return true;
        }
        if (json["action"]?.GetValue<string>() == "Authentication")
        {
            var challenge = json.Deserialize<AuthenticationChallengePayload>(SharedOptions.JsonSerializerOptions)!;
            return await ProcessAuthenticationChallengeAsync(challenge);
        }
        return false;
    }

    async Task<bool> ProcessAuthenticationChallengeAsync(AuthenticationChallengePayload challenge)
    {
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await Shell.Current.GoToAsync("authenticate", new Dictionary<string, object>
            {
                ["payload"] = challenge
            });
            return true;
        }
        return false;
    }

    async Task<bool> ProcessTextAsync(string text)
    {
        try
        {
            text.ToScriptHash(protocolSettings.AddressVersion);
        }
        catch
        {
            return false;
        }
        return await ProcessNeoSchemeAsync(new($"neo:{text}"));
    }
}
