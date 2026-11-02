namespace NeoOrder.OneGate.Controls.Views;

// Hidden optional content has no child controls or bindings until first shown.
// Retain it afterward so recycled rows can reuse it with their new binding context.
public sealed class DeferredContentView : ContentView
{
    public static readonly BindableProperty ContentTemplateProperty = BindableProperty.Create(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(DeferredContentView));

    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        EnsureContent();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == ContentTemplateProperty.PropertyName) Content = null;
        if (propertyName == IsVisibleProperty.PropertyName || propertyName == ContentTemplateProperty.PropertyName)
            EnsureContent();
    }

    void EnsureContent()
    {
        if (Content is null && IsVisible && BindingContext is not null && ContentTemplate is { } template)
            Content = (View)template.CreateContent();
    }
}
