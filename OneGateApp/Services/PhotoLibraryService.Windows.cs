#if WINDOWS

using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace NeoOrder.OneGate.Services;

partial class PhotoLibraryService
{
    public static partial async Task SaveAsync(IScreenshotResult screenshot, string fileName)
    {
        var window = (MauiWinUIWindow)Application.Current!.Windows[0].Handler.PlatformView!;
        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var savePicker = new FileSavePicker(windowId)
        {
            DefaultFileExtension = ".png",
            FileTypeChoices = { ["PNG"] = [".png"] },
            SuggestedFileName = fileName,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        var result = await savePicker.PickSaveFileAsync();
        if (result is null) return;
        using var fileStream = File.Open(result.Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await screenshot.CopyToAsync(fileStream);
    }
}
#endif
