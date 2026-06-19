namespace NeoOrder.OneGate;

static class Workarounds
{
#if WINDOWS
    // This fixed the issue where the Picker's Title would not be shown as a placeholder on Windows.
    // By setting the PlaceholderText of the native control to the Title of the Picker, we ensure that it behaves as expected on Windows.
    // We can remove this workaround once the issue is resolved in .NET MAUI.
    // See https://github.com/dotnet/maui/pull/33007
    public static void FixPickerHandler()
    {
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(nameof(Picker.Title), (handler, view) =>
        {
            if (handler.PlatformView is not null && view is Picker picker && !string.IsNullOrWhiteSpace(picker.Title))
            {
                handler.PlatformView.PlaceholderText = picker.Title;
                picker.Title = null;
            }
        });
    }
#endif
}
