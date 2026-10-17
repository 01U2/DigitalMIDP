using System.Globalization;
using System.Windows.Data;
using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Converters;

/// <summary>
/// Converts a <see cref="SyncedProject"/> into tooltip text for the project sync status pill,
/// including the last local folder activity time when available.
/// </summary>
public sealed class SyncedProjectToTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SyncedProject syncedProject)
        {
            return "Sync status unknown.";
        }

        var lastActivity = syncedProject.LastSyncTime.HasValue
            ? FormatRelativeTime(syncedProject.LastSyncTime.Value)
            : "unknown";

        return syncedProject.Status switch
        {
            SyncStatus.Synced =>
                $"Available locally. Last local folder activity: {lastActivity}. " +
                "Detected from the local Desktop Connector folder, not a live sync signal.",
            SyncStatus.NotSynced =>
                "No local folder found for this project. Add it to Desktop Connector's sync list to make it available locally.",
            SyncStatus.Error =>
                "Could not determine sync status due to an error checking the local folder.",
            _ => "Sync status unknown.",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("SyncedProjectToTooltipConverter supports one-way conversion only.");
    }

    private static string FormatRelativeTime(DateTime utcTime)
    {
        var elapsed = DateTime.UtcNow - utcTime;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            var minutes = (int)elapsed.TotalMinutes;
            return $"{minutes} minute{(minutes == 1 ? string.Empty : "s")} ago";
        }

        if (elapsed.TotalDays < 1)
        {
            var hours = (int)elapsed.TotalHours;
            return $"{hours} hour{(hours == 1 ? string.Empty : "s")} ago";
        }

        var days = (int)elapsed.TotalDays;
        return $"{days} day{(days == 1 ? string.Empty : "s")} ago";
    }
}
