using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DigitalMIDP.Converters;

/// <summary>
/// Shows an element only when the bound value equals the converter parameter.
/// Used by the deliverable wizard to display one step's panel at a time from a single
/// current-step property, rather than carrying a separate visibility flag per step.
/// </summary>
public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString();
        var right = parameter?.ToString();

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(EqualityToVisibilityConverter)} is one-way only.");
    }
}
