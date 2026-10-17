using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Services;

/// <summary>
/// One project's saved column selection: visibility keyed by column name - the fixed keys "Name",
/// "Title", "Size", "Created", "Modified", "PathInProject", "Stage" for the static columns, and a
/// custom attribute's own name (e.g. "Revision", "Status", "Project", "Originator") for every dynamic
/// column. Absent keys fall back to <see cref="ColumnSelectorViewModel"/>'s own defaults rather than
/// being treated as false, so a column added to the app after a preferences file was last saved
/// (or a naming-standard attribute the user has never toggled) still gets a sensible default instead
/// of silently starting hidden.
/// </summary>
public sealed class ProjectColumnPreferences
{
    public Dictionary<string, bool> ColumnVisibility { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Persists each project's column selection to a small local JSON file, so it survives closing and
/// reopening the app - the same one-file-per-preference-kind pattern <see cref="FilterPreferencesStore"/>
/// already established for filters.
/// </summary>
public sealed class ColumnPreferencesStore
{
    private const string FileName = "column-preferences.json";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConstants.AppName,
        FileName);

    private readonly ILogger<ColumnPreferencesStore> _logger;
    private Dictionary<string, ProjectColumnPreferences>? _cache;

    public ColumnPreferencesStore(ILogger<ColumnPreferencesStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Loads the saved column selection for one project, or null when none has been saved yet.</summary>
    public ProjectColumnPreferences? Load(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        var all = LoadAll();
        return all.GetValueOrDefault(projectId);
    }

    /// <summary>Saves one project's column selection, leaving every other project's entry untouched.</summary>
    public void Save(string projectId, ProjectColumnPreferences preferences)
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
            // Not being able to persist a column selection is an inconvenience, not a reason to
            // interrupt the user's work - they keep their selection for the rest of this session,
            // just not across a restart.
            _logger.LogWarning(ex, "Failed to save column preferences to {Path}", FilePath);
        }
    }

    private Dictionary<string, ProjectColumnPreferences> LoadAll()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(FilePath))
        {
            _cache = new Dictionary<string, ProjectColumnPreferences>(StringComparer.OrdinalIgnoreCase);
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            _cache = JsonSerializer.Deserialize<Dictionary<string, ProjectColumnPreferences>>(json)
                     ?? new Dictionary<string, ProjectColumnPreferences>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // A corrupt or hand-edited file should not block the app from starting - start fresh
            // rather than throwing, the same way a missing file is handled.
            _logger.LogWarning(ex, "Failed to read column preferences from {Path}; starting fresh", FilePath);
            _cache = new Dictionary<string, ProjectColumnPreferences>(StringComparer.OrdinalIgnoreCase);
        }

        return _cache;
    }
}
