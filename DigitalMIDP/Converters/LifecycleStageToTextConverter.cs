using System.Globalization;
using System.Windows.Data;
using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converts a file's <see cref="CdeLifecycleStage"/> to display text for the Stage column.
/// </summary>
public sealed class LifecycleStageToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as CdeLifecycleStage?) switch
        {
            CdeLifecycleStage.Wip => "WIP",
            CdeLifecycleStage.Shared => "Shared",
            CdeLifecycleStage.Published => "Published",
            CdeLifecycleStage.Archive => "Archive",
            _ => "—",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("LifecycleStageToTextConverter supports one-way conversion only.");
    }
}
