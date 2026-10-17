using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Services;

/// <summary>
/// One project's saved filter selections: which values are excluded per column, keyed by the same
/// column key <see cref="ViewModels.AttributeFilterViewModel"/> uses (an attribute name, or the
/// fixed keys "Title"/"Stage"), plus the last search text typed.
///
/// Excluded values are stored rather than included ones deliberately: a value that appears later
/// (a new Status option added in ACC, say) then defaults to visible instead of being silently
/// hidden by a filter the user set up before that value existed.
/// </summary>
public sealed class ProjectFilterPreferences
{
    public string? SearchText { get; set; }

    public Dictionary<string, List<string?>> ExcludedValuesByColumn { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Persists each project's filter selections to a small local JSON file, so a filter set up once
/// survives closing and reopening the app - the one persistence requirement this feature has, and
/// the first thing in this codebase that needed any local user-preference storage at all.
/// </summary>
public sealed class FilterPreferencesStore
{
    private const string FileName = "filter-preferences.json";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppName,
        FileName);

    private readonly ILogger<FilterPreferencesStore> _logger;
    private Dictionary<string, ProjectFilterPreferences>? _cache;

    public FilterPreferencesStore(ILogger<FilterPreferencesStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Loads the saved preferences for one project, or null when none have been saved yet.</summary>
    public ProjectFilterPreferences? Load(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var all = LoadAll();
        return all.GetValueOrDefault(projectId);
    }

    /// <summary>Saves one project's preferences, leaving every other project's entry untouched.</summary>
    public void Save(string projectId, ProjectFilterPreferences preferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(preferences);

        var all = LoadAll();
        all[projectId] = preferences;

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            _cache = all;
        }
        catch (Exception ex)
        {
            // Not being able to persist a filter is an inconvenience, not a reason to interrupt the
            // user's work - they can still filter for the rest of this session, just not across a
            // restart.
            _logger.LogWarning(ex, "Failed to save filter preferences to {Path}", FilePath);
        }
    }

    private Dictionary<string, ProjectFilterPreferences> LoadAll()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(FilePath))
        {
            _cache = new Dictionary<string, ProjectFilterPreferences>(StringComparer.OrdinalIgnoreCase);
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            _cache = JsonSerializer.Deserialize<Dictionary<string, ProjectFilterPreferences>>(json)
                     ?? new Dictionary<string, ProjectFilterPreferences>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // A corrupt or hand-edited file should not block the app from starting - start fresh
            // rather than throwing, the same way a missing file is handled.
            _logger.LogWarning(ex, "Failed to read filter preferences from {Path}; starting fresh", FilePath);
            _cache = new Dictionary<string, ProjectFilterPreferences>(StringComparer.OrdinalIgnoreCase);
        }

        return _cache;
    }
}
