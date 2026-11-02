using Android.App;
using Android.Runtime;
using Android.Text.Method;
using Microsoft.Maui.Handlers;

namespace NeoOrder.OneGate.Platforms.Android;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp()
    {
        EditorHandler.Mapper.AppendToMapping(nameof(IEditor.IsReadOnly), (handler, view) =>
        {
            var editText = handler.PlatformView;
            editText.SetTextIsSelectable(view.IsReadOnly);
            // Turning off text selection clears Android's cursor movement method.
            if (!view.IsReadOnly) editText.MovementMethod = ArrowKeyMovementMethod.Instance;
            editText.ShowSoftInputOnFocus = !view.IsReadOnly;
            editText.Focusable = true;
            editText.FocusableInTouchMode = true;
            editText.Clickable = true;
            editText.LongClickable = true;
        });
        return MauiProgram.CreateMauiApp();
    }
}
