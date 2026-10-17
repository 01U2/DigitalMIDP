using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Services;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// One selectable value in a column's filter dropdown, e.g. one Status option or one discipline name.
/// </summary>
public sealed partial class FilterOptionViewModel : ObservableObject
{
    /// <summary>The raw attribute value this option represents. Null means "files with no value set".</summary>
    public string? Value { get; }

    /// <summary>The human-readable description of this value (e.g., "In Progress" for code "001"), if available.</summary>
    public string? Description { get; }

    /// <summary>
    /// What the checkbox list shows - "(Blank)" for a null/empty value, the description if available,
    /// or the value itself as fallback.
    /// Priority: Description > Value > "(Blank)"
    /// </summary>
    public string DisplayText { get; }

    [ObservableProperty]
    private bool isChecked;

    /// <summary>
    /// Creates a filter option with value and optional description.
    /// </summary>
    /// <param name="value">The raw attribute value</param>
    /// <param name="description">Optional human-readable description (e.g., from MetadataOptionDisplayDto.Description)</param>
    /// <param name="isChecked">Whether this option is selected in the filter</param>
    public FilterOptionViewModel(string? value, string? description, bool isChecked)
    {
        Value = value;
        Description = description;
        // Display text priority: use description if available, fall back to value, then "(Blank)"
        DisplayText = !string.IsNullOrWhiteSpace(description)
            ? description
            : (string.IsNullOrEmpty(value) ? "(Blank)" : value);
        this.isChecked = isChecked;
    }

    /// <summary>
    /// Creates a filter option without description (backward compatible).
    /// </summary>
    public FilterOptionViewModel(string? value, bool isChecked)
        : this(value, null, isChecked)
    {
    }
}

/// <summary>
/// The filter for one grid column: every distinct value currently in the grid for that column, each
/// with a checkbox, plus the dropdown's open/closed state.
///
/// Attached to a column's header (see MainWindow's shared filter header template) rather than the
/// column itself, since <see cref="System.Windows.Controls.DataGridColumn"/> has no natural per-column
/// state of its own to hang this off - the column's <c>Header</c> is set to this object directly, with
/// a <c>HeaderTemplate</c> rendering it.
/// </summary>
public sealed partial class AttributeFilterViewModel : ObservableObject
{
    /// <summary>
    /// Stable identity for this column - a custom attribute's name, or a fixed key such as "Deliverable Title"/"Stage"
    /// for the two non-attribute columns that still get a filter. Used as the persistence key and to
    /// carry a user's selection across a rebuild triggered by reloading the folder.
    /// </summary>
    public string ColumnKey { get; }

    public string DisplayName { get; }

    private readonly Func<ItemDto, string?> _valueSelector;

    public ObservableCollection<FilterOptionViewModel> Options { get; } = [];

    [ObservableProperty]
    private bool isPopupOpen;

    /// <summary>True once at least one value has been unchecked - drives the header's "filter applied" highlight.</summary>
    public bool IsActive => Options.Any(o => !o.IsChecked);

    /// <summary>Raised whenever a checkbox changes, so the owning <see cref="FileFilterViewModel"/> re-filters and persists.</summary>
    public event Action? Changed;

    public AttributeFilterViewModel(string columnKey, string displayName, Func<ItemDto, string?> valueSelector)
    {
        ColumnKey = columnKey;
        DisplayName = displayName;
        _valueSelector = valueSelector;
    }

    /// <summary>
    /// Recomputes the distinct values available for this column from the current grid contents.
    /// <paramref name="excludedValues"/> carries over which values were unchecked before this rebuild
    /// (from the user's prior selection, or from saved preferences on the very first build) - a value
    /// not in that set defaults to checked, including one that has never been seen before, so a
    /// newly-appearing attribute value is visible by default rather than silently filtered out.
    /// 
    /// <paramref name="optionDescriptions"/> optionally maps values to human-readable descriptions
    /// (e.g., code "001" → description "In Progress" for Status attributes). When provided, descriptions
    /// are used for display in the filter dropdown while filtering still uses the raw value.
    /// 
    /// <paramref name="predefinedOptions"/> optionally provides the complete set of valid option values
    /// from a metadata field definition. When provided, these are used as the definitive list of display options
    /// (filtered for non-null/non-empty values), rather than deriving options solely from the items' values.
    /// </summary>
    public void RebuildOptions(
        IEnumerable<ItemDto> items,
        IReadOnlySet<string?> excludedValues,
        IReadOnlyDictionary<string?, string?>? optionDescriptions = null,
        IEnumerable<string?>? predefinedOptions = null)
    {
        foreach (var option in Options)
        {
            option.PropertyChanged -= OnOptionPropertyChanged;
        }

        // Use predefined options if available, otherwise derive from items
        IEnumerable<string?> distinctValues;
        if (predefinedOptions != null)
        {
            // Filter out null and empty values from predefined options
            distinctValues = predefinedOptions
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            distinctValues = items
                .Select(_valueSelector)
                .Distinct()
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        Options.Clear();
        foreach (var value in distinctValues)
        {
            // Look up description if available, otherwise null
            var description = optionDescriptions?.GetValueOrDefault(value);
            var option = new FilterOptionViewModel(value, description, isChecked: !excludedValues.Contains(value));
            option.PropertyChanged += OnOptionPropertyChanged;
            Options.Add(option);
        }

        OnPropertyChanged(nameof(IsActive));
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterOptionViewModel.IsChecked))
        {
            OnPropertyChanged(nameof(IsActive));
            Changed?.Invoke();
        }
    }

    /// <summary>True when this column's filter would include the given item.</summary>
    public bool Matches(ItemDto item)
    {
        if (!IsActive)
        {
            return true;
        }

        var value = _valueSelector(item);
        var option = Options.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.Ordinal));
        return option?.IsChecked ?? true;
    }

    /// <summary>Unchecked values, for persistence - see the class doc-comment on why exclusions are what's saved.</summary>
    public IReadOnlySet<string?> GetExcludedValues() =>
        Options.Where(o => !o.IsChecked).Select(o => o.Value).ToHashSet();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var option in Options)
        {
            option.IsChecked = true;
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var option in Options)
        {
            option.IsChecked = false;
        }
    }

    [RelayCommand]
    private void TogglePopup() => IsPopupOpen = !IsPopupOpen;
}

/// <summary>
/// Coordinates the files grid's search box and every column's filter, and persists the selection per
/// project so it survives closing and reopening the app.
///
/// Owns no data of its own beyond filter state - <see cref="Rebuild"/> is called by
/// <see cref="FilesViewModel"/> whenever the underlying items or the set of available custom
/// attributes changes, and <see cref="Matches"/> is the single predicate the grid's
/// <see cref="System.ComponentModel.ICollectionView"/> filter delegates to.
/// </summary>
public sealed partial class FileFilterViewModel : ObservableObject
{
    private readonly FilterPreferencesStore _store;
    private readonly ILogger<FileFilterViewModel> _logger;
    private string? _projectId;
    private bool _isRebuilding;

    [ObservableProperty]
    private string? searchText;

    /// <summary>One entry per filterable column: Deliverable Title, Stage, then every custom attribute currently shown.</summary>
    public ObservableCollection<AttributeFilterViewModel> ColumnFilters { get; } = [];

    /// <summary>
    /// The default static columns' filters, exposed by name (rather than by position in
    /// <see cref="ColumnFilters"/>) so the static XAML columns can bind directly to a stable
    /// property instead of an index that would break if the list's composition ever changes. Dynamic
    /// custom-attribute columns don't need this - MainWindow looks them up by <c>ColumnKey</c> in
    /// code, since they don't exist until the project's naming standard says so.
    /// </summary>
    public AttributeFilterViewModel? NameFilter => ColumnFilters.FirstOrDefault(f => f.ColumnKey == ColumnKeys.Name);

    public AttributeFilterViewModel? TitleFilter => ColumnFilters.FirstOrDefault(f => f.ColumnKey == ColumnKeys.DeliverableTitle);

    public AttributeFilterViewModel? StageFilter => ColumnFilters.FirstOrDefault(f => f.ColumnKey == ColumnKeys.Stage);

    public AttributeFilterViewModel? StatusFilter => ColumnFilters.FirstOrDefault(f => f.ColumnKey == ColumnKeys.Status);

    public AttributeFilterViewModel? RevisionFilter => ColumnFilters.FirstOrDefault(f => f.ColumnKey == ColumnKeys.Revision);

    public AttributeFilterViewModel? PathInProjectFilter => ColumnFilters.FirstOrDefault(f => f.ColumnKey == ColumnKeys.PathInProject);

    /// <summary>True when a search term is typed or any column has a value unchecked - drives the "Clear filters" button's visibility.</summary>
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText) || ColumnFilters.Any(f => f.IsActive);

    /// <summary>Raised whenever anything that affects which rows should show has changed.</summary>
    public event Action? FiltersChanged;

    public FileFilterViewModel(FilterPreferencesStore store, ILogger<FileFilterViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Switches which project's saved preferences apply. Call before the first <see cref="Rebuild"/>
    /// for a newly opened project; a no-op if the project hasn't actually changed, so reloading the
    /// same project's folder doesn't reset filters the user just set.
    /// </summary>
    public void SetProject(string? projectId)
    {
        if (string.Equals(_projectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _projectId = projectId;

        foreach (var filter in ColumnFilters)
        {
            filter.Changed -= OnColumnFilterChanged;
        }
        ColumnFilters.Clear();

        SearchText = null;
    }

    /// <summary>
    /// Rebuilds every column filter's distinct-value list from the current grid contents, preserving
    /// each column's existing selection across the rebuild (or, on the very first rebuild for a
    /// project, applying whatever was saved from a previous session).
    /// </summary>
    public void Rebuild(IReadOnlyList<ItemDto> items, IReadOnlyList<MetadataFieldDisplayDto> availableMetadataFields)
    {
        _isRebuilding = true;
        try
        {
            var persisted = _projectId is not null ? _store.Load(_projectId) : null;
            var previousExcluded = ColumnFilters.ToDictionary(
                f => f.ColumnKey, f => f.GetExcludedValues(), StringComparer.OrdinalIgnoreCase);

            var statusOptions = GetOptionMetadata(availableMetadataFields.FirstOrDefault(
                field => string.Equals(field.Name, ColumnKeys.Status, StringComparison.OrdinalIgnoreCase)));
            var revisionOptions = GetOptionMetadata(availableMetadataFields.FirstOrDefault(
                field => string.Equals(field.Name, ColumnKeys.Revision, StringComparison.OrdinalIgnoreCase)));

            IReadOnlySet<string?> ExcludedFor(string key)
            {
                if (previousExcluded.TryGetValue(key, out var prev))
                {
                    return prev;
                }

                if (persisted?.ExcludedValuesByColumn.TryGetValue(key, out var saved) == true)
                {
                    return saved.ToHashSet();
                }

                return new HashSet<string?>();
            }

            var newFilters = new List<AttributeFilterViewModel>
            {
                BuildFilter(ColumnKeys.Name, "Name", i => i.Name, items, ExcludedFor),
                BuildFilter(ColumnKeys.DeliverableTitle, "Deliverable Title", i => i.CustomAttributes?.GetValueOrDefault(ColumnKeys.DeliverableTitle), items, ExcludedFor),
                BuildFilter(ColumnKeys.Stage, "Stage", i => DescribeStage(i.LifecycleStage), items, ExcludedFor),
                BuildFilter(
                    ColumnKeys.Status,
                    "Status",
                    i => i.CustomAttributes?.GetValueOrDefault(ColumnKeys.Status),
                    items,
                    ExcludedFor,
                    statusOptions.Descriptions,
                    statusOptions.Values),
                BuildFilter(
                    ColumnKeys.Revision,
                    "Revision",
                    i => i.CustomAttributes?.GetValueOrDefault(ColumnKeys.Revision),
                    items,
                    ExcludedFor,
                    revisionOptions.Descriptions,
                    revisionOptions.Values),
                BuildFilter(ColumnKeys.PathInProject, "Path in Project", i => i.PathInProject, items, ExcludedFor)
            };

            foreach (var field in availableMetadataFields)
            {
                var key = field.Name;

                // Static-column keys (including Deliverable Title and Stage) must not get a second
                // filter even when they appear in AvailableMetadataFields. This checks by
                // ColumnKey against what's already been added - a hard crash from
                // ToDictionary further down, over a naming collision this app can otherwise recover
                // from by simply not adding the second one, is a worse outcome than a project with an
                // unusual naming standard just not getting a filter for one duplicate-named attribute.
                if (newFilters.Any(f => string.Equals(f.ColumnKey, key, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning(
                        "Skipping duplicate column filter for '{Key}' - a filter with this key already exists", key);
                    continue;
                }

                var (optionDescriptions, predefinedOptionValues) = GetOptionMetadata(field);

                newFilters.Add(BuildFilter(
                    key,
                    field.Name,
                    i => i.CustomAttributes?.GetValueOrDefault(key),
                    items,
                    ExcludedFor,
                    optionDescriptions,
                    predefinedOptionValues));
            }

            foreach (var filter in ColumnFilters)
            {
                filter.Changed -= OnColumnFilterChanged;
            }

            ColumnFilters.Clear();
            foreach (var filter in newFilters)
            {
                filter.Changed += OnColumnFilterChanged;
                ColumnFilters.Add(filter);
            }

            _logger.LogInformation(
                "Rebuilt {ColumnCount} column filter(s) from {ItemCount} item(s): [{Columns}]",
                newFilters.Count,
                items.Count,
                string.Join(", ", newFilters.Select(f => $"{f.ColumnKey}={f.Options.Count} option(s)")));

            if (SearchText is null && persisted?.SearchText is not null)
            {
                // Persist() itself no-ops while _isRebuilding is true, so setting the property
                // normally here doesn't re-save what was just loaded.
                SearchText = persisted.SearchText;
            }
        }
        finally
        {
            _isRebuilding = false;
        }

        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(TitleFilter));
        OnPropertyChanged(nameof(StageFilter));
    }

    private static (IReadOnlyDictionary<string?, string?>? Descriptions, IReadOnlyList<string?>? Values)
        GetOptionMetadata(MetadataFieldDisplayDto? field)
    {
        if (field is null)
        {
            return (null, null);
        }

        var descriptions = field.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .ToDictionary(option => option.Value!, option => option.Description, StringComparer.OrdinalIgnoreCase);
        var values = field.Options.Select(option => option.Value).ToList();

        return (
            descriptions.Count > 0 ? descriptions : null,
            values.Count > 0 ? values : null);
    }

    private static AttributeFilterViewModel BuildFilter(
        string key,
        string displayName,
        Func<ItemDto, string?> valueSelector,
        IReadOnlyList<ItemDto> items,
        Func<string, IReadOnlySet<string?>> excludedFor,
        IReadOnlyDictionary<string?, string?>? optionDescriptions = null,
        IEnumerable<string?>? predefinedOptions = null)
    {
        var filter = new AttributeFilterViewModel(key, displayName, valueSelector);
        filter.RebuildOptions(items, excludedFor(key), optionDescriptions, predefinedOptions);
        return filter;
    }

    private void OnColumnFilterChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        Persist();
        FiltersChanged?.Invoke();
    }

    partial void OnSearchTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        Persist();
        FiltersChanged?.Invoke();
    }

    [RelayCommand]
    private void ClearAllFilters()
    {
        SearchText = null;

        foreach (var filter in ColumnFilters)
        {
            filter.SelectAllCommand.Execute(null);
        }
    }

    /// <summary>Backs the small "✕" inside the search box - clears only the search text, leaving column filters alone.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = null;

    /// <summary>The single predicate the grid's collection view filters on.</summary>
    public bool Matches(ItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var title = item.CustomAttributes?.GetValueOrDefault(ColumnKeys.DeliverableTitle);
            var haystack = string.IsNullOrEmpty(title) ? item.Name : $"{item.Name} {title}";
            if (haystack.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        foreach (var filter in ColumnFilters)
        {
            if (!filter.Matches(item))
            {
                return false;
            }
        }

        return true;
    }

    private void Persist()
    {
        if (_isRebuilding || _projectId is null)
        {
            return;
        }

        var preferences = new ProjectFilterPreferences
        {
            SearchText = SearchText,
            ExcludedValuesByColumn = ColumnFilters
                .Where(f => f.IsActive)
                .ToDictionary(f => f.ColumnKey, f => f.GetExcludedValues().ToList(), StringComparer.OrdinalIgnoreCase)
        };

        _store.Save(_projectId, preferences);
    }

    private static string? DescribeStage(CdeLifecycleStage? stage) => stage switch
    {
        CdeLifecycleStage.Wip => "WIP",
        CdeLifecycleStage.Shared => "Shared",
        CdeLifecycleStage.Published => "Published",
        CdeLifecycleStage.Archive => "Archive",
        _ => null
    };
}
