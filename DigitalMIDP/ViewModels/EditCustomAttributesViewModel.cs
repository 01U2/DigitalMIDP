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
/// Bulk edits expose only compatible folder definitions; writes use each target's own ID.
/// </summary>
public sealed partial class EditCustomAttributesViewModel : ObservableObject
{
    private const int MaxConcurrentUpdates = 4;

    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<EditCustomAttributesViewModel> _logger;

    private string? _projectId;
    private IReadOnlyList<AttributeEditTarget> _targets = [];
    private Func<bool> _isCurrent = () => false;
    private bool _hasConfirmations;

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
    /// True only when all requested changes were confirmed.
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
        IReadOnlyList<AttributeEditTarget> targets,
        Func<bool> isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(isCurrent);
        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one file must be selected.", nameof(targets));
        }

        _projectId = projectId;
        _targets = targets;
        _isCurrent = isCurrent;
        _hasConfirmations = false;
        IsMultiFile = targets.Count > 1;
        FileName = IsMultiFile ? $"{targets.Count} files selected" : targets[0].Item.Name;
        ErrorMessage = null;
        SuccessMessage = null;
        SaveSucceeded = false;

        var first = targets[0];
        Fields = new(first.Definitions.Where(f => f.AttributeId > 0 &&
            (!f.Type.Equals("array", StringComparison.OrdinalIgnoreCase) || f.Options.Count > 0) &&
            targets.All(t => t.Definitions.Any(d => d.AttributeId > 0 && d.Name == f.Name && d.Type == f.Type &&
                d.Optional == f.Optional && d.Options.Select(o => o.Value).ToHashSet().SetEquals(f.Options.Select(o => o.Value)))))
            .Select(f => new EditableAttributeFieldViewModel(f, first.Values.GetValueOrDefault(f.AttributeId), IsMultiFile)));

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
        SaveSucceeded = false;

        if (_targets.Count == 0 || string.IsNullOrWhiteSpace(_projectId))
        {
            ErrorMessage = "No file selected.";
            return;
        }

        if (_hasConfirmations || !_isCurrent())
        {
            ErrorMessage = "Refresh and reopen the editor before saving again.";
            return;
        }
        var validationError = Validate();
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            return;
        }

        var changedFields = Fields.Where(f => f.HasChanged).ToDictionary(f => f.Name, f => f.Value);
        if (changedFields.Count == 0)
        {
            ErrorMessage = "No changes to save.";
            return;
        }

        IsSaving = true;

        try
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentUpdates);
            var tasks = _targets.Select(async target =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await UpdateSingleItemAsync(target, changedFields);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            var saved = results.Count(r => r.Success && r.Confirmed?.Count > 0);
            var unchanged = results.Count(r => r.Success && r.Confirmed == null);
            var failures = results.Where(r => !r.Success).ToList();

            var payload = results.Where(r => r.Confirmed?.Count > 0).ToDictionary(r => r.Item.ItemUrn!, r => r.Confirmed!);
            if (payload.Count > 0)
            {
                _hasConfirmations = true;
                AttributesUpdated?.Invoke(payload);
            }

            if (!_isCurrent())
            {
                ErrorMessage = "The project or manifest changed during save. Changes may have reached ACC; refresh and reopen to verify.";
                return;
            }

            if (failures.Count == 0)
            {
                if (payload.Count == 0) { ErrorMessage = "No changes to save."; return; }
                SuccessMessage = unchanged > 0 ? $"Updated {saved} files; {unchanged} already had the requested values." : IsMultiFile
                    ? $"Custom attributes updated successfully for all {saved} files."
                    : "Custom attributes updated successfully.";
                SaveSucceeded = true;
            }
            else
            {
                var failureLines = failures.Select(f => $"'{f.Item.Name}': {f.ErrorMessage}");
                ErrorMessage = saved > 0
                    ? $"Updated {saved} of {results.Length} files. The following failed:{Environment.NewLine}{string.Join(Environment.NewLine, failureLines)}"
                    : string.Join(Environment.NewLine, failureLines);
                SaveSucceeded = false;
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<ItemUpdateResult> UpdateSingleItemAsync(
        AttributeEditTarget target, IReadOnlyDictionary<string, string?> changedFields)
    {
        var item = target.Item;
        if (string.IsNullOrWhiteSpace(item.TipVersionUrn))
        {
            return new ItemUpdateResult(item, false, "File version could not be determined. Reload the folder and try again.");
        }

        try
        {
            var updates = target.Definitions.Where(f => changedFields.ContainsKey(f.Name) &&
                (!target.Values.TryGetValue(f.AttributeId, out var old) || old != changedFields[f.Name]))
                .Select(f => new CustomAttributeUpdateRequestDto(f.AttributeId, changedFields[f.Name]))
                .ToList();
            if (updates.Count == 0) return new(item, true, null);
            if (!_isCurrent()) return new(item, false, "Manifest changed. Reopen the editor.");
            await _dataManagementService.ValidateAttributeEditAsync(_projectId!, target, updates);
            if (!_isCurrent()) return new(item, false, "Manifest changed. Reopen the editor.");
            var results = await _dataManagementService.UpdateItemCustomAttributesAsync(
                _projectId!,
                item.TipVersionUrn!,
                updates,
                CancellationToken.None);

            var confirmed = new Dictionary<string, string?>();
            var missing = new List<string>();
            foreach (var update in updates)
            {
                var name = target.Definitions.Single(d => d.AttributeId == update.AttributeId).Name;
                var matches = results.Where(r => r.AttributeId == update.AttributeId).ToArray();
                if (matches.Length == 1 && matches[0].Value == update.Value)
                    confirmed.Add(name, matches[0].Value);
                else missing.Add(name);
            }
            var unexpected = results.Any(r => !updates.Any(u => u.AttributeId == r.AttributeId));
            var error = missing.Count > 0 ? $"Not confirmed: {string.Join(", ", missing)}. Refresh before retrying." :
                unexpected ? "Unexpected attribute confirmation. Refresh before retrying." : null;
            return new(item, error == null, error, confirmed);
        }
        catch (PermissionDeniedException ex)
        {
            _logger.LogWarning(ex, "Permission denied updating custom attributes for {ItemName}", item.Name);
            return new ItemUpdateResult(item, false, ex.Message);
        }
        catch (AttributeValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error updating custom attributes for {ItemName}", item.Name);
            return new ItemUpdateResult(item, false, BuildValidationErrorMessage(ex, target));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update custom attributes for {ItemName}", item.Name);
            return new ItemUpdateResult(item, false, ex.Message);
        }
    }

    private string? Validate()
    {
        foreach (var field in Fields)
        {
            // In multi-file mode, an untouched field is intentionally left alone on every
            // file, so it isn't sent and doesn't need to satisfy required/option constraints.
            if (!field.HasChanged)
            {
                continue;
            }

            if (!field.Optional && string.IsNullOrWhiteSpace(field.Value))
            {
                return $"'{field.Name}' is required.";
            }

            if (field.IsDropdown
                && !string.IsNullOrWhiteSpace(field.Value)
                && !field.Options.Any(o => string.Equals(o.Value, field.Value, StringComparison.Ordinal)))
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
    private static string BuildValidationErrorMessage(AttributeValidationException ex, AttributeEditTarget target)
    {
        if (ex.Errors.Count == 0)
        {
            return ex.Message;
        }

        var fieldMessages = ex.Errors.Select(error =>
        {
            var fieldName = target.Definitions.FirstOrDefault(f => f.AttributeId == error.AttributeId)?.Name
                ?? $"attribute {error.AttributeId}";
            var reason = error.Detail ?? ex.Message;
            return $"'{fieldName}': {reason}";
        });

        return string.Join(Environment.NewLine, fieldMessages);
    }

    private sealed record ItemUpdateResult(ItemDto Item, bool Success, string? ErrorMessage,
        IReadOnlyDictionary<string, string?>? Confirmed = null);
}
