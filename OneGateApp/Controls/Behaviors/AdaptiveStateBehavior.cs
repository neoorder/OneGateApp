using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Controls.Behaviors;

sealed partial class AdaptiveStateBehavior : Behavior<VisualElement>
{
    enum AdaptiveState
    {
        Compact,
        Expanded,
        Other
    }

    const double Breakpoint = 900;

    VisualElement? _associatedObject;
    Window? _window;
    AdaptiveState? _currentState;

    public Collection<Setter> CompactSetters { get; } = new();
    public Collection<Setter> ExpandedSetters { get; } = new();
    public Collection<Setter> NarrowDeviceSetters { get; } = new();

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedObject = bindable;
        bindable.Loaded += OnLoaded;
        bindable.Unloaded += OnUnloaded;
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.Loaded -= OnLoaded;
        bindable.Unloaded -= OnUnloaded;
        DetachWindow();
        _associatedObject = null;
        base.OnDetachingFrom(bindable);
    }

    void OnLoaded(object? sender, EventArgs e)
    {
        AttachWindow();
        ApplyCurrentState(force: true);
    }

    void OnUnloaded(object? sender, EventArgs e)
    {
        DetachWindow();
    }

    void AttachWindow()
    {
        if (_associatedObject?.Window is null) return;
        if (_window == _associatedObject.Window) return;
        DetachWindow();
        _window = _associatedObject.Window;
        _window.SizeChanged += OnWindowSizeChanged;
        _window.Destroying += OnWindowDestroying;
    }

    void DetachWindow()
    {
        _window?.SizeChanged -= OnWindowSizeChanged;
        _window?.Destroying -= OnWindowDestroying;
        _window = null;
    }

    void OnWindowSizeChanged(object? sender, EventArgs e)
    {
        ApplyCurrentState(force: false);
    }

    void OnWindowDestroying(object? sender, EventArgs e)
    {
        DetachWindow();
    }

    void ApplyCurrentState(bool force)
    {
        if (_associatedObject is null) return;
        var width = GetDecisionWidth(_window);
        if (width <= 0) return;
        var newState = GetState(width);
        if (!force && _currentState == newState) return;
        _currentState = newState;
        var setters = newState switch
        {
            AdaptiveState.Compact => CompactSetters,
            AdaptiveState.Expanded => ExpandedSetters,
            _ => NarrowDeviceSetters
        };
        foreach (var setter in setters)
            ApplySetter(setter);
        _associatedObject.InvalidateMeasure();
    }

    static double GetDecisionWidth(Window? window)
    {
#if MACCATALYST
        if (window?.Handler?.PlatformView is UIKit.UIWindow uiWindow && uiWindow.Bounds.Width > 0)
            return uiWindow.Bounds.Width;
#endif
        if (window?.Width > 0) return window.Width;
        return 0;
    }

    static AdaptiveState GetState(double width)
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Desktop && DeviceInfo.Idiom != DeviceIdiom.Tablet)
            return AdaptiveState.Other;
        return width >= Breakpoint ? AdaptiveState.Expanded : AdaptiveState.Compact;
    }

    void ApplySetter(Setter setter)
    {
        if (_associatedObject is null) return;
        var target = ResolveTarget(setter);
        if (target is null) return;
        target.SetValue(setter.Property, setter.Value);
    }

    BindableObject? ResolveTarget(Setter setter)
    {
        if (_associatedObject is null) return null;
        if (string.IsNullOrWhiteSpace(setter.TargetName))
            return _associatedObject;
        if (_associatedObject is Element element)
            return element.FindByName<BindableObject>(setter.TargetName);
        return null;
    }
}
