using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// One input in the deliverable wizard, backed by an ACC custom attribute.
/// Mirrors <see cref="EditableAttributeFieldViewModel"/>, but for creation rather than editing:
/// there is no original value to compare against.
/// </summary>
public sealed partial class DeliverableFieldViewModel : ObservableObject
{
    public int AttributeId { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool Optional { get; }
    public bool IsComputed { get; }
    public IReadOnlyList<MetadataOptionDisplayDto> Options { get; }

    /// <summary>True when the value must be chosen from <see cref="Options"/>.</summary>
    public bool IsDropdown => Options.Count > 0;

    /// <summary>True when the field accepts free text - the inverse of <see cref="IsDropdown"/>, exposed for binding.</summary>
    public bool IsFreeText => !IsDropdown;

    /// <summary>True when the user may edit this field.</summary>
    public bool IsEditable => !IsComputed;

    /// <summary>Fewest characters ACC accepts, from the project's naming standard. Null when unconstrained.</summary>
    public int? MinLength { get; }

    /// <summary>Most characters ACC accepts. Null when unconstrained.</summary>
    public int? MaxLength { get; }

    /// <summary>
    /// The description shown under the field, with ACC's length rule appended where there is one, so
    /// "2-6 characters" is visible before the user types rather than after the value is refused.
    /// </summary>
    public string? Hint { get; }

    [ObservableProperty]
    private string? value;

    public DeliverableFieldViewModel(
        int attributeId,
        string name,
        string? description,
        bool optional,
        IReadOnlyList<MetadataOptionDisplayDto> options,
        string? initialValue,
        bool isComputed = false,
        int? minLength = null,
        int? maxLength = null,
        string? lengthHint = null)
    {
        AttributeId = attributeId;
        Name = name;
        Description = description;
        Optional = optional;
        Options = options;
        IsComputed = isComputed;
        MinLength = minLength;
        MaxLength = maxLength;
        value = initialValue;

        Hint = (description, lengthHint) switch
        {
            (null or "", null or "") => null,
            (null or "", _) => lengthHint,
            (_, null or "") => description,
            _ => $"{description} ({lengthHint})"
        };
    }
}

/// <summary>
/// One deliverable within a batch: its own editable Deliverable Title, stored as Description.
/// Other metadata and document-number segments are shared across the batch.
/// </summary>
public sealed partial class DeliverableItemViewModel : ObservableObject
{
    /// <summary>1-based position in the batch, used only for the "Document N" label.</summary>
    public int Index { get; }

    public DeliverableFieldViewModel DeliverableTitleField { get; }

    public DeliverableItemViewModel(int index, DeliverableFieldViewModel deliverableTitleField)
    {
        Index = index;
        DeliverableTitleField = deliverableTitleField;
    }

    /// <summary>This item's Description value, keyed by attribute id, ready to merge over shared metadata.</summary>
    public IReadOnlyDictionary<int, string?> CollectValues() =>
        string.IsNullOrWhiteSpace(DeliverableTitleField.Value)
            ? new Dictionary<int, string?>()
            : new Dictionary<int, string?> { [DeliverableTitleField.AttributeId] = DeliverableTitleField.Value.Trim() };
}

/// <summary>
/// ViewModel backing the "New Deliverable" wizard.
///
/// Walks the user through selecting a deliverable type, discipline, and quantity, choosing a
/// template, supplying the remaining document-number segments and per-document titles, and finally
/// reviewing the allocated number range and destination.
///
/// This ViewModel does NOT perform the actual creation. Clicking "Create" packages everything the
/// user entered into a <see cref="CreateDeliverableBatchRequestDto"/>, raises
/// <see cref="CreationRequested"/> with it, and the window closes immediately - the caller (see
/// <c>MainWindow.NewDeliverableButton_Click</c>) is responsible for running the actual batch in the
/// background and reporting its progress, so the user is never stuck watching this dialog while
/// several sequential API calls complete. This split exists because the wizard's own steps are fast
/// (local validation, cached lookups), but the creation itself is not - each document is several HTTP
/// round trips, run one after another by design (see <see cref="ICreateDeliverableUseCase.CreateManyAsync"/>),
/// and there is no reason the user should be locked to this window for that duration.
///
/// A quantity greater than one produces several deliverables that share every attribute except their
/// Deliverable Titles: the same document-number segments, the same Status/Revision/other metadata, the same
/// template and discipline, but each with its own Description and
/// its own sequential document number. This is the batch shape the client asked for directly.
///
/// Number segments, types and disciplines come from the naming standard; related custom attributes,
/// including Deliverable Title, come from the project's separate custom-attribute schema.
/// </summary>
public sealed partial class CreateDeliverableViewModel : ObservableObject
{
    /// <summary>Wizard steps, in order. Bound to from XAML by number.</summary>
    public const int StepTypeAndDiscipline = 1;
    public const int StepTemplate = 2;
    public const int StepDetails = 3;
    public const int StepReview = 4;
    private const int LastStep = StepReview;

    /// <summary>
    /// Upper bound on how many documents can be created in one batch. Creation is necessarily
    /// sequential (see <see cref="ICreateDeliverableUseCase.CreateManyAsync"/>) and each item costs
    /// several HTTP round trips, so an unbounded quantity would let a typo turn into a very long wait
    /// or an accidental flood of files. 25 is generous for the "a handful of documents, one file per
    /// title" scenario this exists for while keeping a mistaken entry cheap to recover from.
    /// </summary>
    public const int MaxQuantity = 25;

    private readonly ICdeStructureUseCase _cdeStructureUseCase;
    private readonly IDataManagementService _dataManagementService;
    private readonly ITokenProvider _tokenProvider;
    private readonly ITemplateCatalogUseCase _templateCatalogUseCase;
    private readonly IDocumentNumberUseCase _documentNumberUseCase;
    private readonly ILogger<CreateDeliverableViewModel> _logger;

    private string? _hubId;
    private string? _projectId;
    private NamingConventionDto _convention = NamingConventionDto.None;
    private IReadOnlyList<MetadataFieldDisplayDto> _relatedMetadata = [];
    private NamingConventionFieldDto? _typeField;
    private NamingConventionFieldDto? _disciplineField;

    /// <summary>
    /// The Description (or Deliverable Title) definition, captured from related project metadata
    /// so each batch item can get its own fresh, independently-editable copy.
    /// A <see cref="DeliverableFieldViewModel"/> holds mutable per-instance state (its Value), so the
    /// same instance cannot be shared across items - every item needs its own.
    /// </summary>
    private MetadataFieldDisplayDto? _deliverableTitleDefinition;

    [ObservableProperty]
    private int currentStep = StepTypeAndDiscipline;

    [ObservableProperty]
    private string stepTitle = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? busyMessage;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Deliverable types offered in step 1, from the naming convention's Type field.</summary>
    [ObservableProperty]
    private ObservableCollection<MetadataOptionDisplayDto> deliverableTypes = [];

    [ObservableProperty]
    private MetadataOptionDisplayDto? selectedDeliverableType;

    /// <summary>Disciplines offered in step 1, from the naming convention's Role field.</summary>
    [ObservableProperty]
    private ObservableCollection<DisciplineFolderDto> disciplines = [];

    [ObservableProperty]
    private DisciplineFolderDto? selectedDiscipline;

    /// <summary>
    /// Raw text of the quantity input, bound directly to the TextBox. Kept separate from
    /// <see cref="Quantity"/> so a value the user is still typing (or has temporarily made invalid,
    /// e.g. clearing the box) does not immediately collapse the quantity or rebuild
    /// <see cref="DeliverableItems"/> mid-keystroke; <see cref="Quantity"/> only updates once the text
    /// parses to a number in range.
    /// </summary>
    [ObservableProperty]
    private string quantityText = "1";

    /// <summary>
    /// How many deliverables this run of the wizard creates. Always at least 1; changing it rebuilds
    /// <see cref="DeliverableItems"/> to match.
    /// </summary>
    [ObservableProperty]
    private int quantity = 1;

    /// <summary>True once quantity is more than 1, so the UI can label items ("Document 1 of 5") only when that's actually informative.</summary>
    public bool IsBatch => Quantity > 1;

    /// <summary>Backs the quantity stepper's minus button.</summary>
    public bool CanDecrementQuantity => Quantity > 1;

    /// <summary>Backs the quantity stepper's plus button.</summary>
    public bool CanIncrementQuantity => Quantity < MaxQuantity;

    /// <summary>Label for the final action button - "Create Deliverable" or "Create 5 Deliverables".</summary>
    public string CreateButtonLabel => IsBatch ? $"Create {Quantity} Deliverables" : "Create Deliverable";

    [ObservableProperty]
    private ObservableCollection<DeliverableTemplateDto> templates = [];

    [ObservableProperty]
    private DeliverableTemplateDto? selectedTemplate;

    /// <summary>The remaining document-number segments the user fills in at step 3.</summary>
    [ObservableProperty]
    private ObservableCollection<DeliverableFieldViewModel> numberFields = [];

    /// <summary>
    /// Metadata shared by every item in the batch - Status, Revision, and any other project-defined
    /// metadata except Description. Deliverable Titles live separately in <see cref="DeliverableItems"/>.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DeliverableFieldViewModel> sharedMetadataFields = [];

    /// <summary>
    /// One entry per document being created (count == <see cref="Quantity"/>), each with its own
    /// Deliverable Title input. Rebuilt whenever <see cref="Quantity"/> changes.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DeliverableItemViewModel> deliverableItems = [];

    /// <summary>The first document number that will be allocated, resolved when the review step is reached.</summary>
    [ObservableProperty]
    private string? previewDocumentNumber;

    /// <summary>
    /// The last document number in the batch, shown only when <see cref="IsBatch"/> - computed
    /// locally from the first number plus (Quantity - 1) rather than by allocating all of them, since
    /// allocating for real is exactly what creation itself does a moment later.
    /// </summary>
    [ObservableProperty]
    private string? previewLastDocumentNumber;

    [ObservableProperty]
    private string? previewFileName;

    [ObservableProperty]
    private string? previewDestination;

    [ObservableProperty]
    private bool destinationFolderWillBeCreated;

    /// <summary>True while the wizard has enough configuration to proceed at all.</summary>
    [ObservableProperty]
    private bool isConfigured;

    public bool IsFirstStep => CurrentStep == StepTypeAndDiscipline;

    public bool IsLastStep => CurrentStep == LastStep;

    /// <summary>Disabled only while a background load (initial configuration, or the review preview) is in flight.</summary>
    public bool CanCreate => !IsBusy;

    // Navigation visibility, computed here rather than combined in XAML so the rule for each button
    // lives in one place instead of a multi-binding converter.

    public bool ShowBack => !IsFirstStep;

    public bool ShowNext => !IsLastStep;

    public bool ShowCreate => IsLastStep;

    /// <summary>
    /// Raised when the user clicks Create with valid input. Carries the finished batch request; the
    /// window closes immediately afterward (see <c>CreateDeliverableWindow</c>'s constructor) without
    /// waiting for anything to actually be created - the subscriber (MainWindow) owns running the
    /// batch and reporting its progress once this dialog is gone.
    /// </summary>
    public event Action<CreateDeliverableBatchRequestDto>? CreationRequested;

    public CreateDeliverableViewModel(
        ICdeStructureUseCase cdeStructureUseCase,
        IDataManagementService dataManagementService,
        ITokenProvider tokenProvider,
        ITemplateCatalogUseCase templateCatalogUseCase,
        IDocumentNumberUseCase documentNumberUseCase,
        ILogger<CreateDeliverableViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(cdeStructureUseCase);
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(templateCatalogUseCase);
        ArgumentNullException.ThrowIfNull(documentNumberUseCase);
        ArgumentNullException.ThrowIfNull(logger);

        _cdeStructureUseCase = cdeStructureUseCase;
        _dataManagementService = dataManagementService;
        _tokenProvider = tokenProvider;
        _templateCatalogUseCase = templateCatalogUseCase;
        _documentNumberUseCase = documentNumberUseCase;
        _logger = logger;

        UpdateStepTitle();
    }

    /// <summary>
    /// Loads the project's naming convention, disciplines and templates. Must be awaited before the
    /// dialog is shown.
    /// </summary>
    public async Task InitializeAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        _hubId = hubId;
        _projectId = projectId;

        IsBusy = true;
        BusyMessage = "Loading project configuration...";
        ErrorMessage = null;

        try
        {
            // Routed through the cached use case rather than the data service directly - resolving
            // the convention costs several HTTP round trips, and GetStructureAsync below needs the
            // same convention internally, so calling the data service here would fetch it twice.
            _convention = await _cdeStructureUseCase.GetNamingConventionAsync(hubId, projectId, cancellationToken);

            if (!_convention.IsConfigured)
            {
                ErrorMessage = "No naming standard is configured for this project. Set one up in Project Admin.";
                IsConfigured = false;
                return;
            }

            _typeField = _convention.TypeField;
            _disciplineField = _convention.DisciplineField;

            if (_disciplineField is null)
            {
                ErrorMessage = "The naming standard has no Role field. Add one in ACC.";
                IsConfigured = false;
                return;
            }

            // Fetched per call, never stored on the ViewModel - see TokenExposureTests.
            var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            var structure = await _cdeStructureUseCase.GetStructureAsync(
                hubId, projectId, accessToken, cancellationToken);

            if (structure.Wip is null)
            {
                ErrorMessage = "No WIP folder was found in this project.";
                IsConfigured = false;
                return;
            }

            // Related custom attributes are not necessarily listed in the naming standard's
            // metadata. Use the same project/top-folder schema as Edit Attributes and the grid;
            // the matching related attribute id is the one ACC expects on the new file version.
            var relatedSchema = await _dataManagementService.GetProjectNamingStandardAsync(
                hubId, projectId, cancellationToken);
            _relatedMetadata = relatedSchema.Metadata;
            _deliverableTitleDefinition = _relatedMetadata.FirstOrDefault(f =>
                f.AttributeId != 0 && string.Equals(f.Name, DeliverableAttributeNames.Description, StringComparison.OrdinalIgnoreCase))
                ?? _relatedMetadata.FirstOrDefault(f =>
                    f.AttributeId != 0 && string.Equals(f.Name, "Deliverable Title", StringComparison.OrdinalIgnoreCase));
            if (_deliverableTitleDefinition is null)
            {
                ErrorMessage = "No Description or Deliverable Title attribute is available in the project's related custom attributes. Configure one in ACC.";
                IsConfigured = false;
                return;
            }

            Disciplines = new ObservableCollection<DisciplineFolderDto>(structure.Disciplines);
            DeliverableTypes = new ObservableCollection<MetadataOptionDisplayDto>(_typeField?.Options ?? []);

            BusyMessage = "Loading templates...";
            var templates = await _templateCatalogUseCase.GetTemplatesAsync(
                hubId, projectId, accessToken, cancellationToken);
            Templates = new ObservableCollection<DeliverableTemplateDto>(templates);

            if (Templates.Count == 0)
            {
                ErrorMessage = structure.TemplatesFolder is null
                    ? "No templates folder was found in this project."
                    : $"'{structure.TemplatesFolder.Name}' has no templates in it.";
            }

            BuildFields();
            IsConfigured = true;

            _logger.LogInformation(
                "Deliverable wizard initialised: {TypeCount} type(s), {DisciplineCount} discipline(s), {TemplateCount} template(s)",
                DeliverableTypes.Count, Disciplines.Count, Templates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise the deliverable wizard for project {ProjectId}", projectId);
            ErrorMessage = $"Could not load the project's configuration: {ex.Message}";
            IsConfigured = false;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    /// <summary>
    /// Builds the step-3 inputs: the document-number segments not already chosen in step 1, the
    /// naming-standard and related attributes shared across the batch, and the related title definition
    /// each batch item clones its own copy from. The sequential number is excluded because the application
    /// allocates it, and Type/Role are excluded because they are step 1's selections.
    /// </summary>
    private void BuildFields()
    {
        var handledElsewhere = new[] { _typeField?.AttributeId, _disciplineField?.AttributeId, _convention.SequentialNumberField?.AttributeId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        NumberFields = new ObservableCollection<DeliverableFieldViewModel>(
            _convention.Fields
                .Where(f => !handledElsewhere.Contains(f.AttributeId))
                .Select(f => new DeliverableFieldViewModel(
                    f.AttributeId, f.Name, null, f.Optional, f.Options, f.DefaultValue,
                    isComputed: false,
                    minLength: f.MinLength,
                    maxLength: f.MaxLength,
                    lengthHint: f.LengthHint)));

        SharedMetadataFields = new ObservableCollection<DeliverableFieldViewModel>(
            _convention.Metadata
                .Concat(_relatedMetadata.Where(f => !_convention.Metadata.Any(existing => existing.AttributeId == f.AttributeId)))
                .Where(f => f.AttributeId != 0
                            && !handledElsewhere.Contains(f.AttributeId)
                            && !string.Equals(f.Name, DeliverableAttributeNames.Description, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(f.Name, "Deliverable Title", StringComparison.OrdinalIgnoreCase))
                .Select(f => new DeliverableFieldViewModel(
                    f.AttributeId,
                    f.Name,
                    f.Description,
                    f.Optional,
                    f.Options,
                    ResolveInitialValue(f))));

        RebuildDeliverableItems(Quantity);
    }

    /// <summary>
    /// Rebuilds <see cref="DeliverableItems"/> to hold exactly <paramref name="quantity"/> items, each
    /// with its own fresh Description field. Called once from <see cref="BuildFields"/> for the
    /// initial quantity of 1, and again whenever the user changes the quantity.
    ///
    /// Existing entries are retained when quantity changes, so going back to add more documents
    /// does not discard the titles already entered.
    /// </summary>
    private void RebuildDeliverableItems(int quantity)
    {
        if (_deliverableTitleDefinition is null)
        {
            return;
        }

        var items = new List<DeliverableItemViewModel>(quantity);

        for (var i = 1; i <= quantity; i++)
        {
            if (i <= DeliverableItems.Count)
            {
                items.Add(DeliverableItems[i - 1]);
                continue;
            }

            var field = _deliverableTitleDefinition;
            items.Add(new DeliverableItemViewModel(i, new DeliverableFieldViewModel(
                field.AttributeId, "Deliverable Title", field.Description, false, field.Options, field.DefaultValue)));
        }

        DeliverableItems = new ObservableCollection<DeliverableItemViewModel>(items);
    }

    /// <summary>
    /// Chooses a field's starting value: the workflow default (Status "S0", Revision "P01.01") where
    /// the project actually permits it, otherwise whatever ACC itself defaults to.
    ///
    /// The permission check matters. Those two constants come from ISO 19650, not from this project's
    /// configuration, and a project whose Status list spells its options differently ("S0 - WIP", or
    /// codes of its own) would otherwise start the user off with a value ACC is certain to reject -
    /// and the rejection only surfaces after the file has been created.
    /// </summary>
    private static string? ResolveInitialValue(MetadataFieldDisplayDto field)
    {
        var workflowDefault = DeliverableAttributeNames.DefaultValueFor(field.Name);

        if (workflowDefault is null)
        {
            return field.DefaultValue;
        }

        // A free-text field accepts anything; a list-constrained one only its own options.
        var allowed = field.Options.Count == 0
            || field.Options.Any(o => string.Equals(o.Value, workflowDefault, StringComparison.OrdinalIgnoreCase));

        return allowed ? workflowDefault : field.DefaultValue;
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        ErrorMessage = null;

        var validationError = ValidateCurrentStep();
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            return;
        }

        if (CurrentStep >= LastStep)
        {
            return;
        }

        CurrentStep++;

        if (CurrentStep == StepReview)
        {
            await LoadReviewAsync();
        }
    }

    [RelayCommand]
    private void Back()
    {
        ErrorMessage = null;

        if (CurrentStep > StepTypeAndDiscipline)
        {
            CurrentStep--;
        }
    }

    /// <summary>
    /// Resolves the number(s) and destination shown on the review step, so the user sees exactly what
    /// will be created before committing. The number is allocated again during creation - ACC cannot
    /// reserve one - so this is a preview, and a concurrent creation could still shift it. For a batch,
    /// only the first item's number is actually allocated here; the rest of the range is computed
    /// locally (see <see cref="ComputeLastNumberPreview"/>) rather than performed for real, since doing
    /// that for real is exactly what creation itself does moments later.
    /// </summary>
    private async Task LoadReviewAsync()
    {
        if (_projectId is null || SelectedDiscipline is null)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Allocating document number...";

        try
        {
            var accessToken = await _tokenProvider.GetAccessTokenAsync(CancellationToken.None);
            var documentNumber = await _documentNumberUseCase.AllocateNextAsync(
                _projectId, SelectedDiscipline, _convention, CollectFieldValues(), accessToken);

            PreviewDocumentNumber = documentNumber.Value;
            PreviewLastDocumentNumber = ComputeLastNumberPreview(documentNumber, Quantity);
            PreviewFileName = $"{documentNumber.Value}{SelectedTemplate?.Extension ?? string.Empty}";
            PreviewDestination = $"WIP/{SelectedDiscipline.ExpectedFolderName}";
            DestinationFolderWillBeCreated = !SelectedDiscipline.Exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to allocate a document number for preview");
            ErrorMessage = $"Could not allocate a document number: {ex.Message}";
            PreviewDocumentNumber = null;
            PreviewLastDocumentNumber = null;
            PreviewFileName = null;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    /// <summary>
    /// Computes the last number in a batch by adding (quantity - 1) to the first number's sequence and
    /// re-padding to the same width, without allocating anything. Returns null for a single-item batch,
    /// where there is no range to show, or when the sequence segment is not purely numeric (defensive -
    /// every sequence this application allocates is numeric, but a hand-edited or unusual convention
    /// is not something to guess at).
    /// </summary>
    private static string? ComputeLastNumberPreview(DocumentNumberDto first, int quantity)
    {
        if (quantity <= 1 || first.Segments.Count == 0)
        {
            return null;
        }

        var lastSegment = first.Segments[^1];
        if (!int.TryParse(lastSegment.Value, out var startSequence))
        {
            return null;
        }

        var lastValue = (startSequence + quantity - 1).ToString().PadLeft(lastSegment.Value.Length, '0');

        var segments = first.Segments
            .Take(first.Segments.Count - 1)
            .Append(lastSegment with { Value = lastValue })
            .ToList();

        return new DocumentNumberDto(segments, first.Delimiter).Value;
    }

    /// <summary>
    /// Validates and packages the batch request, then hands it off via <see cref="CreationRequested"/>.
    /// Deliberately synchronous and does not touch the network: the actual creation is the caller's
    /// responsibility, run after this dialog has already closed. See the class doc comment for why.
    /// </summary>
    [RelayCommand]
    private void Create()
    {
        ErrorMessage = null;

        if (_hubId is null || _projectId is null
            || SelectedDiscipline is null || SelectedTemplate is null || DeliverableItems.Count == 0)
        {
            ErrorMessage = "The wizard is missing information needed to create the file.";
            return;
        }

        var request = new CreateDeliverableBatchRequestDto(
            HubId: _hubId,
            ProjectId: _projectId,
            // Left empty on purpose: the dialog never holds a token. MainWindow.RunDeliverableBatchAsync
            // attaches a current one at the moment the batch actually runs.
            AccessToken: string.Empty,
            Template: SelectedTemplate,
            DisciplineCode: SelectedDiscipline.Code,
            FieldValues: CollectFieldValues(),
            SharedMetadataValues: CollectSharedMetadataValues(),
            PerItemTitleValues: DeliverableItems.Select(item => item.CollectValues()).ToList());

        CreationRequested?.Invoke(request);
    }

    /// <summary>
    /// Gathers the document-number segment values: the type and discipline chosen in step 1, plus
    /// the fields filled in at step 3.
    /// </summary>
    private Dictionary<int, string> CollectFieldValues()
    {
        var values = new Dictionary<int, string>();

        if (_typeField is not null && !string.IsNullOrWhiteSpace(SelectedDeliverableType?.Value))
        {
            values[_typeField.AttributeId] = SelectedDeliverableType!.Value!;
        }

        if (_disciplineField is not null && SelectedDiscipline is not null)
        {
            values[_disciplineField.AttributeId] = SelectedDiscipline.Code;
        }

        foreach (var field in NumberFields.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
        {
            values[field.AttributeId] = field.Value!.Trim();
        }

        return values;
    }

    /// <summary>
    /// The metadata values shared by every item in the batch - Status, Revision, and anything else
    /// that is not Description. Deliverable Titles are collected separately, per item, by
    /// <see cref="DeliverableItemViewModel.CollectValues"/>.
    /// </summary>
    private Dictionary<int, string?> CollectSharedMetadataValues()
    {
        return SharedMetadataFields
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .ToDictionary(f => f.AttributeId, f => (string?)f.Value!.Trim());
    }

    private string? ValidateCurrentStep()
    {
        switch (CurrentStep)
        {
            case StepTypeAndDiscipline:
                if (DeliverableTypes.Count > 0 && SelectedDeliverableType is null)
                {
                    return "Select a deliverable type.";
                }

                if (SelectedDiscipline is null)
                {
                    return "Select a discipline.";
                }

                if (!int.TryParse(QuantityText, out var requestedQuantity) || requestedQuantity < 1)
                {
                    return "Enter a quantity of at least 1.";
                }

                if (requestedQuantity > MaxQuantity)
                {
                    return $"Maximum {MaxQuantity} documents at once.";
                }

                return null;

            case StepTemplate:
                return SelectedTemplate is null ? "Select a template." : null;

            case StepDetails:
                foreach (var field in NumberFields.Where(f => !f.Optional && string.IsNullOrWhiteSpace(f.Value)))
                {
                    return $"'{field.Name}' is required.";
                }

                foreach (var field in NumberFields.Where(f => f.IsDropdown && !string.IsNullOrWhiteSpace(f.Value)))
                {
                    if (!field.Options.Any(o => string.Equals(o.Value, field.Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"'{field.Value}' is not a valid option for '{field.Name}'.";
                    }
                }

                // Free-text segments still have to be legal in a document number and in a file name.
                foreach (var field in NumberFields.Where(f => f.IsFreeText))
                {
                    var problem = DocumentNumberSegmentValidator.Validate(
                        field.Name, field.Value, _convention.Delimiter, field.MinLength, field.MaxLength);

                    if (problem is not null)
                    {
                        return problem;
                    }
                }

                foreach (var field in SharedMetadataFields.Where(f => f.IsEditable && !f.Optional && string.IsNullOrWhiteSpace(f.Value)))
                {
                    return $"'{field.Name}' is required.";
                }

                // Metadata dropdowns need the same option check as the number fields. Without it a
                // value ACC will reject - including a workflow default like "S0" on a project whose
                // Status list spells its options differently - reaches the server and fails the whole
                // attribute batch after the file has already been created.
                foreach (var field in SharedMetadataFields.Where(f => f.IsEditable && f.IsDropdown && !string.IsNullOrWhiteSpace(f.Value)))
                {
                    if (!field.Options.Any(o => string.Equals(o.Value, field.Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"'{field.Value}' is not a valid option for '{field.Name}'. "
                             + $"Choose one of: {string.Join(", ", field.Options.Take(10).Select(o => o.Value))}.";
                    }
                }

                // Every document needs its own Description, displayed as Deliverable Title. Attribute
                // options, when configured, are checked before any file is created in ACC.
                foreach (var item in DeliverableItems)
                {
                    var itemLabel = IsBatch ? $"Document {item.Index}: " : string.Empty;
                    var field = item.DeliverableTitleField;
                    if (string.IsNullOrWhiteSpace(field.Value))
                    {
                        return $"{itemLabel}Deliverable Title is required.";
                    }

                    if (field.IsDropdown && !field.Options.Any(o => string.Equals(o.Value, field.Value, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"{itemLabel}'{field.Value}' is not a valid option for Deliverable Title.";
                    }
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses the quantity text and, only on a valid in-range value, updates <see cref="Quantity"/>
    /// (which in turn rebuilds <see cref="DeliverableItems"/>). An invalid or out-of-range value is
    /// left for <see cref="ValidateCurrentStep"/> to report when the user tries to advance, rather
    /// than being clamped or rejected mid-keystroke - clamping "3" to "1" while the user is still
    /// typing "30" would fight the user's own typing.
    /// </summary>
    partial void OnQuantityTextChanged(string value)
    {
        if (int.TryParse(value, out var parsed) && parsed >= 1 && parsed <= MaxQuantity)
        {
            Quantity = parsed;
        }
    }

    partial void OnQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(IsBatch));
        OnPropertyChanged(nameof(CreateButtonLabel));
        OnPropertyChanged(nameof(CanDecrementQuantity));
        OnPropertyChanged(nameof(CanIncrementQuantity));
        RebuildDeliverableItems(value);
        UpdateStepTitle();
    }

    /// <summary>
    /// The stepper buttons go through <see cref="QuantityText"/> rather than setting
    /// <see cref="Quantity"/> directly, so there is exactly one path that turns typed or clicked input
    /// into a quantity change. Enabled state is a plain XAML binding to
    /// <see cref="CanDecrementQuantity"/>/<see cref="CanIncrementQuantity"/> (matching every other
    /// button in this dialog) rather than a RelayCommand CanExecute, which would need an explicit
    /// NotifyCanExecuteChanged call on every Quantity change to stay in sync.
    /// </summary>
    [RelayCommand]
    private void DecrementQuantity() => QuantityText = Math.Max(Quantity - 1, 1).ToString();

    [RelayCommand]
    private void IncrementQuantity() => QuantityText = Math.Min(Quantity + 1, MaxQuantity).ToString();

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepTitle();
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(ShowBack));
        OnPropertyChanged(nameof(ShowNext));
        OnPropertyChanged(nameof(ShowCreate));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCreate));

    private void UpdateStepTitle()
    {
        StepTitle = CurrentStep switch
        {
            StepTypeAndDiscipline => "Step 1 of 4 — Deliverable type, discipline, and quantity",
            StepTemplate => "Step 2 of 4 — Select a template",
            StepDetails => IsBatch
                ? $"Step 3 of 4 — Document number and Deliverable Titles for {Quantity} documents"
                : "Step 3 of 4 — Document number and Deliverable Title",
            StepReview => "Step 4 of 4 — Review and create",
            _ => string.Empty
        };
    }
}
