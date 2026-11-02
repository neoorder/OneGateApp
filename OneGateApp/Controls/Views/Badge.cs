using System.Globalization;

namespace NeoOrder.OneGate.Controls.Views;

public sealed partial class Badge : Border
{
    // Shell tabs use the same count property as the visual badge control.
    public static readonly BindableProperty CountProperty = BindableProperty.CreateAttached(
        nameof(Count), typeof(long), typeof(Badge), 0L, propertyChanged: OnCountChanged);

    // Runtime XAML paths cannot read attached properties; this binding also observes Count changes.
    public static BindingBase AttachedCountBinding => new Microsoft.Maui.Controls.Internals.TypedBinding<BindableObject, long>(
        source => (GetCount(source), true), null,
        [new Tuple<Func<BindableObject, object>, string>(source => source, CountProperty.PropertyName)]);

    readonly Label label = new() { StyleClass = ["Badge"] };

    public long Count
    {
        get => GetCount(this);
        set => SetCount(this, value);
    }

    public Badge()
    {
        Content = label;
        UpdateCount();
    }

    public static long GetCount(BindableObject target) => (long)target.GetValue(CountProperty);
    public static void SetCount(BindableObject target, long value) => target.SetValue(CountProperty, value);

    internal static string? FormatCount(long count) => count <= 0 ? null
        : count > 99 ? "99+" : count.ToString(CultureInfo.CurrentCulture);

    static void OnCountChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is Badge badge) badge.UpdateCount();
    }

    void UpdateCount()
    {
        label.Text = FormatCount(Count);
        IsVisible = Count > 0;
    }
}
