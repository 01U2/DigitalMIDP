using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using DigitalMIDP.Services;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// ViewModel for managing file list column visibility, and persisting the selection per project (see
/// <see cref="ColumnPreferencesStore"/>) the same way <see cref="FileFilterViewModel"/> already
/// persists filter selections.
/// </summary>
public sealed partial class ColumnSelectorViewModel : ObservableObject
{
    /// <summary>
    /// Attribute names that default to visible the first time a project is opened (before any column
    /// selection has been saved for it) - every other dynamic attribute column, including the newly
    /// added naming-code columns (Project, Originator, Volume/System, Level/Location, ...), defaults
    /// to hidden so the grid isn't overwhelming on first use.
    /// </summary>
    private static readonly HashSet<string> DefaultVisibleAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ColumnKeys.Revision,
        ColumnKeys.Status
    };

    private readonly ColumnPreferencesStore? _store;
    private string? _projectId;
    private bool _isApplyingPreferences;

    [ObservableProperty]
    private bool isNameColumnVisible = true;

    /// <summary>Despite the name, this is the Deliverable Title column's visibility - kept as-is since it's already the bound XAML property name.</summary>
    [ObservableProperty]
    private bool isTypeColumnVisible = true;

    [ObservableProperty]
    private bool isSizeColumnVisible;

    [ObservableProperty]
    private bool isCreatedColumnVisible;

    [ObservableProperty]
    private bool isModifiedColumnVisible;

    [ObservableProperty]
    private bool isLocationColumnVisible;

    [ObservableProperty]
    private bool isStageColumnVisible = true;

    [ObservableProperty]
    private bool isStatusColumnVisible = true;

    [ObservableProperty]
    private bool isRevisionColumnVisible = true;

    /// <summary>
    /// Indicates whether custom attributes are currently loading from APS.
    /// </summary>
    [ObservableProperty]
    private bool isCustomAttributesLoading;

    /// <summary>
    /// Tracks visibility state for each custom (and naming-code) attribute column, keyed by attribute
    /// name. Also doubles as "has this attribute's default already been decided" - see
    /// <see cref="EnsureCustomAttributeColumnDefaultVisibility"/>.
    /// </summary>
    private readonly Dictionary<string, bool> _customAttributeColumnVisibility = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parameterless for design-time/XAML tooling; production code should use the store-backed constructor.</summary>
    public ColumnSelectorViewModel()
    {
    }

    public ColumnSelectorViewModel(ColumnPreferencesStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Switches which project's saved column selection applies - mirrors
    /// <see cref="FileFilterViewModel.SetProject"/>. A no-op if the project hasn't actually changed,
    /// so reloading the same project's folder doesn't reset columns the user just set.
    /// </summary>
    public void SetProject(string? projectId)
    {
        if (string.Equals(_projectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _projectId = projectId;
        _customAttributeColumnVisibility.Clear();

        var persisted = (_store is not null && projectId is not null) ? _store.Load(projectId) : null;

        _isApplyingPreferences = true;
        try
        {
            IsNameColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Name) ?? true;
            IsTypeColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.DeliverableTitle) ?? true;
            IsSizeColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Size) ?? false;
            IsCreatedColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Created) ?? false;
            IsModifiedColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Modified) ?? false;
            IsLocationColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.PathInProject) ?? true;
            IsStageColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Stage) ?? true;
            IsStatusColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Status) ?? true;
            IsRevisionColumnVisible = persisted?.ColumnVisibility.GetValueOrDefault(ColumnKeys.Revision) ?? true;

            if (persisted is not null)
            {
                foreach (var (name, isVisible) in persisted.ColumnVisibility)
                {
                    if (!IsStaticColumnKey(name))
                    {
                        _customAttributeColumnVisibility[name] = isVisible;
                    }
                }
            }
        }
        finally
        {
            _isApplyingPreferences = false;
        }
    }

    private static bool IsStaticColumnKey(string name) =>
        name is ColumnKeys.Name or ColumnKeys.DeliverableTitle or ColumnKeys.Size or ColumnKeys.Created
            or ColumnKeys.Modified or ColumnKeys.PathInProject or ColumnKeys.Stage or ColumnKeys.Status or ColumnKeys.Revision;

    /// <summary>
    /// Gets the visibility state for a custom (or naming-code) attribute column.
    /// </summary>
    public bool GetCustomAttributeColumnVisibility(string columnName)
    {
        return _customAttributeColumnVisibility.TryGetValue(columnName, out var isVisible) && isVisible;
    }

    /// <summary>
    /// Sets the visibility state for a custom attribute column, e.g. from a column selector menu click,
    /// and persists it.
    /// </summary>
    public void SetCustomAttributeColumnVisibility(string columnName, bool isVisible)
    {
        _customAttributeColumnVisibility[columnName] = isVisible;
        Persist();
    }

    /// <summary>
    /// Assigns this attribute column's default visibility the first time it's seen for the current
    /// project (no persisted or previously-toggled value yet) - true for <see cref="DefaultVisibleAttributeNames"/>
    /// (Revision, Status), false for everything else, including every naming-code column. A no-op if a
    /// value already exists, so a rebuild triggered by a folder reload never overwrites the user's
    /// actual selection - see the bug this replaced, where every column refresh forced every dynamic
    /// column back to hidden regardless of what was persisted or just clicked.
    /// </summary>
    public void EnsureCustomAttributeColumnDefaultVisibility(string columnName)
    {
        if (_customAttributeColumnVisibility.ContainsKey(columnName))
        {
            return;
        }

        _customAttributeColumnVisibility[columnName] = DefaultVisibleAttributeNames.Contains(columnName);
    }

    /// <summary>
    /// Clears all custom attribute column visibility state.
    /// </summary>
    public void ClearCustomAttributeVisibility()
    {
        _customAttributeColumnVisibility.Clear();
    }

    partial void OnIsNameColumnVisibleChanged(bool value) => Persist();
    partial void OnIsTypeColumnVisibleChanged(bool value) => Persist();
    partial void OnIsSizeColumnVisibleChanged(bool value) => Persist();
    partial void OnIsCreatedColumnVisibleChanged(bool value) => Persist();
    partial void OnIsModifiedColumnVisibleChanged(bool value) => Persist();
    partial void OnIsLocationColumnVisibleChanged(bool value) => Persist();
    partial void OnIsStageColumnVisibleChanged(bool value) => Persist();
    partial void OnIsStatusColumnVisibleChanged(bool value) => Persist();
    partial void OnIsRevisionColumnVisibleChanged(bool value) => Persist();

    private void Persist()
    {
        if (_isApplyingPreferences || _store is null || _projectId is null)
        {
            return;
        }

        var preferences = new ProjectColumnPreferences
        {
            ColumnVisibility = new Dictionary<string, bool>(_customAttributeColumnVisibility, StringComparer.OrdinalIgnoreCase)
            {
                [ColumnKeys.Name] = IsNameColumnVisible,
                [ColumnKeys.DeliverableTitle] = IsTypeColumnVisible,
                [ColumnKeys.Size] = IsSizeColumnVisible,
                [ColumnKeys.Created] = IsCreatedColumnVisible,
                [ColumnKeys.Modified] = IsModifiedColumnVisible,
                [ColumnKeys.PathInProject] = IsLocationColumnVisible,
                [ColumnKeys.Stage] = IsStageColumnVisible,
                [ColumnKeys.Status] = IsStatusColumnVisible,
                [ColumnKeys.Revision] = IsRevisionColumnVisible
            }
        };

        _store.Save(_projectId, preferences);
    }
}
