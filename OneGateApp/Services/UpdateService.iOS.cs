#if IOS || MACCATALYST

using Foundation;
using NeoOrder.OneGate.Pages;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services;

partial class UpdateService
{
    private partial async Task<bool> CheckForUpdatesInternalAsync()
    {
        if (IsAppStore())
        {
            JsonObject result = (await httpClient.GetFromJsonAsync<JsonObject>("https://itunes.apple.com/lookup?id=1584915425"))!;
            Version latest = new(result["results"]![0]!["version"]!.GetValue<string>());
            return latest > AppInfo.Version;
        }
        return await CheckForUpdatesFallbackAsync();
    }

    private partial async Task UpdateInternalAsync()
    {
        if (IsAppStore())
        {
            await Commands.LaunchUrl.ExecuteAsync("https://apps.apple.com/app/id1584915425");
            IsUpdating = false;
        }
        else
        {
            await UpdateFallbackAsync();
        }
    }

    static bool IsAppStore()
    {
        return NSBundle.MainBundle.AppStoreReceiptUrl?.LastPathComponent == "receipt";
    }
}
#endif
