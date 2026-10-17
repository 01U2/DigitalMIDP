using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// A single editable custom attribute field shown in the Edit Attributes dialog.
/// In multi-file mode the field starts blank and is only sent to the server if the
/// user actually types/selects a value ("touched") — untouched fields leave each
/// file's existing value alone.
/// </summary>
public sealed partial class EditableAttributeFieldViewModel : ObservableObject
{
    public int AttributeId { get; }
    public string Name { get; }
    public string Type { get; }
    public string? Description { get; }
    public bool Optional { get; }
    public IReadOnlyList<MetadataOptionDisplayDto> Options { get; }

    /// <summary>
    /// True when this field's values must be chosen from <see cref="Options"/> (dropdown/array type).
    /// </summary>
    public bool IsDropdown => Options.Count > 0;

    private readonly string? _originalValue;
    private readonly bool _isMultiFile;
    private readonly bool _initializing;

    [ObservableProperty]
    private string? value;

    [ObservableProperty]
    private bool touched;

    public EditableAttributeFieldViewModel(MetadataFieldDisplayDto field, string? currentValue, bool isMultiFile)
    {
        AttributeId = field.AttributeId;
        Name = field.Name;
        Type = field.Type;
        Description = field.Description;
        Optional = field.Optional;
        Options = field.Options;
        _originalValue = currentValue;
        _isMultiFile = isMultiFile;

        _initializing = true;
        value = isMultiFile ? null : currentValue;
        _initializing = false;
    }

    partial void OnValueChanged(string? value)
    {
        if (!_initializing && _isMultiFile)
        {
            Touched = true;
        }
    }

    /// <summary>
    /// Whether this field should be included in the update sent to the server:
    /// for a single file, any edit away from the original value; for multiple
    /// files, only fields the user explicitly touched.
    /// </summary>
    public bool HasChanged => _isMultiFile
        ? Touched
        : !string.Equals(Value, _originalValue, StringComparison.Ordinal);
}

/// <summary>
/// ViewModel backing the "Edit Attributes" dialog. Lets the user change one or more
/// selected files' ACC custom attribute values and push the change to ACC/BIM 360.
/// Attribute schema is scoped per project, so the same field set applies to every
/// selected file regardless of how many are chosen.
/// </summary>
public sealed partial class EditCustomAttributesViewModel : ObservableObject
{
    private const int MaxConcurrentUpdates = 4;

    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<EditCustomAttributesViewModel> _logger;

    private string? _projectId;
    private IReadOnlyList<ItemDto> _items = [];
    private bool _isMultiFile;

    [ObservableProperty]
    private string? fileName;

    [ObservableProperty]
    private bool isMultiFile;

    [ObservableProperty]
    private ObservableCollection<EditableAttributeFieldViewModel> fields = [];

    [ObservableProperty]
    private bool isSaving;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? successMessage;

    /// <summary>
    /// Set to true after at least one file saved successfully so the view can close the dialog.
    /// </summary>
    [ObservableProperty]
    private bool saveSucceeded;

    /// <summary>
    /// Raised after a save with the updated attribute values for each successfully-updated file,
    /// keyed by item URN, so the caller can refresh its own cache/grid without a full reload.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>>? AttributesUpdated;

    public EditCustomAttributesViewModel(
        IDataManagementService dataManagementService,
        ILogger<EditCustomAttributesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(logger);

        _dataManagementService = dataManagementService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the dialog for one or more selected files. Must be called before the dialog is shown.
    /// For a single file, fields are pre-filled with its current values. For multiple files, fields
    /// start blank — only fields the user explicitly edits are applied, leaving every other attribute
    /// untouched on each file.
    /// </summary>
    public void Initialize(
        string projectId,
        IReadOnlyList<ItemDto> items,
        IReadOnlyList<MetadataFieldDisplayDto> availableFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(availableFields);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one file must be selected.", nameof(items));
        }

        _projectId = projectId;
        _items = items;
        _isMultiFile = items.Count > 1;
        IsMultiFile = _isMultiFile;
        FileName = _isMultiFile ? $"{items.Count} files selected" : items[0].Name;
        ErrorMessage = null;
        SuccessMessage = null;
        SaveSucceeded = false;

        var currentValues = _isMultiFile
            ? new Dictionary<string, string?>()
            : items[0].CustomAttributes ?? new Dictionary<string, string?>();

        _logger.LogInformation(
            "EditCustomAttributesViewModel.Initialize received {Count} available fields: [{Fields}]",
            availableFields.Count,
            string.Join(", ", availableFields.Select(f => $"{f.Name}(id={f.AttributeId})")));

        Fields = new ObservableCollection<EditableAttributeFieldViewModel>(
            availableFields
                .Where(f => f.AttributeId != 0)
                .Select(f => new EditableAttributeFieldViewModel(
                    f,
                    currentValues.TryGetValue(f.Name, out var v) ? v : null,
                    _isMultiFile)));

        _logger.LogInformation(
            "EditCustomAttributesViewModel.Initialize built {Count} editable fields: [{Fields}]",
            Fields.Count,
            string.Join(", ", Fields.Select(f => $"{f.Name}(id={f.AttributeId})")));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;

        if (_items.Count == 0 || string.IsNullOrWhiteSpace(_projectId))
        {
            ErrorMessage = "No file selected.";
            return;
        }

        var validationError = Validate();
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            return;
        }

        var changedFields = Fields.Where(f => f.HasChanged).ToList();
        if (changedFields.Count == 0)
        {
            ErrorMessage = "No changes to save.";
            return;
        }

        IsSaving = true;

        try
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentUpdates);
            var tasks = _items.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await UpdateSingleItemAsync(item, changedFields);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            var successes = results.Where(r => r.Success).ToList();
            var failures = results.Where(r => !r.Success).ToList();

            if (successes.Count > 0)
            {
                var changedValues = changedFields.ToDictionary(f => f.Name, f => f.Value, StringComparer.OrdinalIgnoreCase);
                var payload = successes.ToDictionary(
                    s => s.Item.ItemUrn!,
                    s => MergeValues(s.Item, changedValues),
                    StringComparer.OrdinalIgnoreCase);
                AttributesUpdated?.Invoke(payload);
            }

            if (failures.Count == 0)
            {
                SuccessMessage = _isMultiFile
                    ? $"Custom attributes updated successfully for all {successes.Count} files."
                    : "Custom attributes updated successfully.";
                SaveSucceeded = true;
            }
            else
            {
                var failureLines = failures.Select(f => $"'{f.Item.Name}': {f.ErrorMessage}");
                ErrorMessage = successes.Count > 0
                    ? $"Updated {successes.Count} of {results.Length} files. The following failed:{Environment.NewLine}{string.Join(Environment.NewLine, failureLines)}"
                    : string.Join(Environment.NewLine, failureLines);
                SaveSucceeded = successes.Count > 0;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<ItemUpdateResult> UpdateSingleItemAsync(
        ItemDto item, IReadOnlyList<EditableAttributeFieldViewModel> changedFields)
    {
        if (string.IsNullOrWhiteSpace(item.TipVersionUrn))
        {
            return new ItemUpdateResult(item, false, "File version could not be determined. Reload the folder and try again.");
        }

        try
        {
            var updates = changedFields
                .Select(f => new CustomAttributeUpdateRequestDto(f.AttributeId, f.Value))
                .ToList();

            await _dataManagementService.UpdateItemCustomAttributesAsync(
                _projectId!,
                item.TipVersionUrn!,
                updates,
                CancellationToken.None);

            return new ItemUpdateResult(item, true, null);
        }
        catch (PermissionDeniedException ex)
        {
            _logger.LogWarning(ex, "Permission denied updating custom attributes for {ItemName}", item.Name);
            return new ItemUpdateResult(item, false, ex.Message);
        }
        catch (AttributeValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error updating custom attributes for {ItemName}", item.Name);
            return new ItemUpdateResult(item, false, BuildValidationErrorMessage(ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update custom attributes for {ItemName}", item.Name);
            return new ItemUpdateResult(item, false, ex.Message);
        }
    }

    private static IReadOnlyDictionary<string, string?> MergeValues(
        ItemDto item, IReadOnlyDictionary<string, string?> changedValues)
    {
        var merged = new Dictionary<string, string?>(
            item.CustomAttributes ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in changedValues)
        {
            merged[name] = value;
        }

        return merged;
    }

    private string? Validate()
    {
        foreach (var field in Fields)
        {
            // In multi-file mode, an untouched field is intentionally left alone on every
            // file, so it isn't sent and doesn't need to satisfy required/option constraints.
            if (_isMultiFile && !field.Touched)
            {
                continue;
            }

            if (!field.Optional && string.IsNullOrWhiteSpace(field.Value))
            {
                return $"'{field.Name}' is required.";
            }

            if (field.IsDropdown
                && !string.IsNullOrWhiteSpace(field.Value)
                && !field.Options.Any(o => string.Equals(o.Value, field.Value, StringComparison.OrdinalIgnoreCase)))
            {
                return $"'{field.Value}' is not a valid option for '{field.Name}'.";
            }
        }

        return null;
    }

    /// <summary>
    /// Turns ACC's structured validation error (attribute id + reason) into a message naming the
    /// actual field the user edited, instead of showing the raw API error id/JSON.
    /// </summary>
    private string BuildValidationErrorMessage(AttributeValidationException ex)
    {
        if (ex.Errors.Count == 0)
        {
            return ex.Message;
        }

        var fieldMessages = ex.Errors.Select(error =>
        {
            var fieldName = Fields.FirstOrDefault(f => f.AttributeId == error.AttributeId)?.Name
                ?? $"attribute {error.AttributeId}";
            var reason = error.Detail ?? ex.Message;
            return $"'{fieldName}': {reason}";
        });

        return string.Join(Environment.NewLine, fieldMessages);
    }

    private sealed record ItemUpdateResult(ItemDto Item, bool Success, string? ErrorMessage);
}
