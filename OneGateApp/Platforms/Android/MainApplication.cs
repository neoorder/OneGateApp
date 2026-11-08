using Android.App;
using Android.Runtime;
using Android.Text.Method;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace NeoOrder.OneGate.Platforms.Android;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp()
    {
        static void ConfigureEditor(IEditorHandler handler, IEditor view)
        {
            var editText = handler.PlatformView;
            editText.SetTextIsSelectable(view.IsReadOnly);
            // Focus permits selection; removing the key listener also blocks paste and hardware input.
            if (view.IsReadOnly) editText.KeyListener = null;
            else if (editText.KeyListener is null) editText.UpdateKeyboard(view);
            // Turning off text selection clears Android's cursor movement method.
            if (!view.IsReadOnly) editText.MovementMethod = ArrowKeyMovementMethod.Instance;
            editText.ShowSoftInputOnFocus = !view.IsReadOnly;
            editText.SetCursorVisible(!view.IsReadOnly);
            editText.Focusable = true;
            editText.FocusableInTouchMode = true;
            editText.Clickable = true;
            editText.LongClickable = true;
        }
        EditorHandler.Mapper.AppendToMapping(nameof(IEditor.IsReadOnly), ConfigureEditor);
        EditorHandler.Mapper.AppendToMapping(nameof(IEditor.Keyboard), ConfigureEditor);
        EditorHandler.Mapper.AppendToMapping(nameof(IEditor.IsTextPredictionEnabled), ConfigureEditor);
        EditorHandler.Mapper.AppendToMapping(nameof(IEditor.IsSpellCheckEnabled), ConfigureEditor);
        return MauiProgram.CreateMauiApp();
    }
}
