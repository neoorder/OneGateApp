using CommunityToolkit.Maui.Behaviors;

namespace NeoOrder.OneGate.Controls.Behaviors;

// These controls only need tap and long-press commands. Use native Android gestures
// so unloading a recycled row cannot dispose a Toolkit accessibility listener with queued callbacks.
public sealed class PressBehavior : TouchBehavior
{
#if ANDROID
    VisualElement? element;
    bool wasClickable;
    bool wasLongClickable;

    protected override void OnAttachedTo(VisualElement bindable, Android.Views.View platformView)
    {
        element = bindable;
        wasClickable = platformView.Clickable;
        wasLongClickable = platformView.LongClickable;
        platformView.Touch += OnTouch;
        platformView.Click += OnClick;
        platformView.LongClick += OnLongClick;
        platformView.Clickable = true;
        platformView.LongClickable = true;
    }

    protected override void OnDetachedFrom(VisualElement bindable, Android.Views.View platformView)
    {
        element = null;
        platformView.Touch -= OnTouch;
        platformView.Click -= OnClick;
        platformView.LongClick -= OnLongClick;
        platformView.Clickable = wasClickable;
        platformView.LongClickable = wasLongClickable;
    }

    static void OnTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        // The desktop-only MAUI tap recognizer must not consume Android's native gestures.
        e.Handled = false;
    }

    void OnClick(object? sender, EventArgs e)
    {
        if (IsEnabled && element?.IsEnabled == true && Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
    }

    void OnLongClick(object? sender, Android.Views.View.LongClickEventArgs e)
    {
        e.Handled = IsEnabled && element?.IsEnabled == true
            && LongPressCommand?.CanExecute(LongPressCommandParameter) == true;
        if (e.Handled) LongPressCommand!.Execute(LongPressCommandParameter);
    }
#endif
}
