using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converts a string to Visibility based on whether it has any content.
/// Null or empty -> Collapsed, anything else -> Visible.
///
/// Distinct from <see cref="NullToVisibilityConverter"/>: a TextBox bound with
/// UpdateSourceTrigger=PropertyChanged sets its source to "" (not null) once the user deletes all
/// typed text, and NullToVisibilityConverter's null-only check would leave a "clear" button visible
/// on an already-empty box.
/// </summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("StringNotEmptyToVisibilityConverter supports one-way conversion only.");
    }
}
