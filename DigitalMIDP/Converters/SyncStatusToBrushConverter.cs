using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converts a Desktop Connector <see cref="SyncStatus"/> to a background Brush for status pills.
/// </summary>
public sealed class SyncStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SyncedBrush = new(Color.FromRgb(0x27, 0xAE, 0x60));
    private static readonly SolidColorBrush NotSyncedBrush = new(Color.FromRgb(0xE6, 0x7E, 0x22));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromRgb(0x95, 0xA5, 0xA6));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as SyncStatus?) switch
        {
            SyncStatus.Synced => SyncedBrush,
            SyncStatus.NotSynced => NotSyncedBrush,
            SyncStatus.Error => ErrorBrush,
            _ => UnknownBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("SyncStatusToBrushConverter supports one-way conversion only.");
    }
}
