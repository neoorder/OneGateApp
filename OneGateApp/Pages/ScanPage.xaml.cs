using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using SkiaSharp;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using ZXing.Common;
using ZXing.Net.Maui;
using ZXing.SkiaSharp;

namespace NeoOrder.OneGate.Pages;

public partial class ScanPage : ContentPage, IQueryAttributable
{
    readonly ProtocolSettings protocolSettings;
    string? action;

    public ScanPage(ProtocolSettings protocolSettings)
    {
        this.protocolSettings = protocolSettings;
        InitializeComponent();
        cameraView.Options = cameraView.Options with
        {
            Formats = BarcodeFormat.QrCode
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("action", out var action))
            this.action = (string)action;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        cameraView.IsDetecting = false;
    }

    async void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        cameraView.IsDetecting = false;
        if (MainThread.IsMainThread)
        {
            await ProcessScanResultAsync(e.Results[0].Value);
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() => ProcessScanResultAsync(e.Results[0].Value));
        }
    }

    async void OnPickPhotoClicked(object sender, EventArgs e)
    {
        MediaPickerOptions options = new()
        {
            Title = Strings.ScanFromPhotoLibrary
        };
        List<FileResult> results = await MediaPicker.PickPhotosAsync(options);
        if (results.Count == 0) return;
        await using var stream = await results[0].OpenReadAsync();
        using var managedStream = new SKManagedStream(stream);
        using var bitmap = SKBitmap.Decode(managedStream);
        if (bitmap == null)
        {
            await Toast.Show(Strings.ErrorMessageUnableReadQRCode);
            return;
        }
        var reader = new BarcodeReader
        {
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [ZXing.BarcodeFormat.QR_CODE]
            },
            AutoRotate = true
        };
        var result = reader.Decode(bitmap);
        if (result == null || string.IsNullOrWhiteSpace(result.Text))
        {
            await Toast.Show(Strings.ErrorMessageUnableReadQRCode);
            return;
        }
        await ProcessScanResultAsync(result.Text);
    }

    void OnTorchClicked(object sender, EventArgs e)
    {
        cameraView.IsTorchOn = !cameraView.IsTorchOn;
    }

    async Task ProcessScanResultAsync(string result)
    {
        try
        {
            if (Uri.TryCreate(result, UriKind.Absolute, out var uri))
            {
                if (await ProcessUriAsync(uri)) return;
            }
            else if (result.StartsWith('{'))
            {
                JsonObject? json = JsonSerializer.Deserialize<JsonObject>(result, SharedOptions.JsonSerializerOptions);
                if (json is not null && await ProcessJsonAsync(json)) return;
            }
            else
            {
                if (await ProcessTextAsync(result)) return;
            }
        }
        catch
        {
        }
        string errorMessage = string.Format(Strings.ErrorMessageUnsupportedQRCode, result);
        await DisplayAlertAsync(null, errorMessage, Strings.OK);
        cameraView.IsDetecting = true;
    }

    async Task<bool> ProcessUriAsync(Uri uri)
    {
        return uri.Scheme switch
        {
            "neo" => await ProcessNeoSchemeAsync(uri),
            "https" => await ProcessHttpsSchemeAsync(uri),
            _ => false,
        };
    }

    async Task<bool> ProcessNeoSchemeAsync(Uri uri)
    {
        try
        {
            uri.LocalPath.ToScriptHash(protocolSettings.AddressVersion);
        }
        catch
        {
            return false;
        }
        var nv = HttpUtility.ParseQueryString(uri.Query);
        string query = "?address=" + uri.LocalPath;
        if (nv["asset"] is string s_asset)
            query += $"&asset={UInt160.Parse(s_asset)}";
        if (nv["amount"] is string s_amount)
            query += $"&amount={decimal.Parse(s_amount)}";
        switch (action)
        {
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
        if (uri.Host == SharedOptions.OneGateDomain)
        {
            if (await ProcessDappUriAsync(uri)) return true;
            if (await ProcessNewsUriAsync(uri)) return true;
        }
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await Browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
            return true;
        }
        return false;
    }

    async Task<bool> ProcessDappUriAsync(Uri uri)
    {
        if (!DAppLaunchUri.TryGetAppId(uri, out _)) return false;
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await Commands.LaunchDApp.ExecuteAsync(uri);
            return true;
        }
        return false;
    }

    async Task<bool> ProcessNewsUriAsync(Uri uri)
    {
        if (uri.Segments.Length != 3) return false;
        if (uri.Segments[1] != "news/") return false;
        if (!int.TryParse(uri.Segments[2], out int appId)) return false;
        if (appId <= 0) return false;
        if (action is null)
        {
            await Shell.Current.GoToAsync("..");
            await Shell.Current.GoToAsync("//home/news/details", new Dictionary<string, object>
            {
                ["uri"] = uri
            });
            return true;
        }
        return false;
    }

    async Task<bool> ProcessJsonAsync(JsonObject json)
    {
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
