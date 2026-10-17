using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converts null values to Visibility.
/// Null -> Collapsed, Not Null -> Visible.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
