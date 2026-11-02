using System.Globalization;

namespace NeoOrder.OneGate.Controls.Converters;

class TransparentColorConverter : IValueConverter
{
    // Preserve RGB so interpolation fades opacity without adding a grey or black tint.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color color ? color.WithAlpha(0) : Colors.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
