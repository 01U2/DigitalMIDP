using System.Globalization;
using System.Windows.Data;
using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converts a Desktop Connector <see cref="SyncStatus"/> to display text for status pills.
/// </summary>
public sealed class SyncStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as SyncStatus?) switch
        {
            SyncStatus.Synced => "Synced",
            SyncStatus.NotSynced => "Not Synced",
            SyncStatus.Error => "Sync check failed",
            _ => "Sync status unknown",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("SyncStatusToTextConverter supports one-way conversion only.");
    }
}
