using System.Globalization;

namespace NeoOrder.OneGate.Controls.Views;

class TabBarBackgroundColorConverter : IMultiValueConverter
{
    static readonly Color Transparent = Colors.Transparent;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return Transparent;
        if (values[0] is not string value) return Transparent;
        if (values[1] is not string selectedTab) return Transparent;
        if (values[2] is not Color selectedTabBackgroundColor) return Transparent;
        if (value != selectedTab) return Transparent;
        return selectedTabBackgroundColor;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return null!;
    }
}
