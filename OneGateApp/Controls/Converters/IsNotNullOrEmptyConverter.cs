using System.Globalization;

namespace NeoOrder.OneGate.Controls.Converters;

class IsNotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            string text => !string.IsNullOrWhiteSpace(text),
            null => false,
            _ => true
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
