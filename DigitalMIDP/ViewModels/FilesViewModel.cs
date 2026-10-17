using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// ViewModel for managing file and folder listing and navigation.
/// Also handles loading and displaying custom attribute fields from ACC results.
/// </summary>
public sealed partial class FilesViewModel : ObservableObject, IDisposable
{
    private readonly IFileListingUseCase _fileListingUseCase;
    private readonly IDataManagementService _dataManagementService;
    private readonly IOpenFileLocallyUseCase _openFileLocallyUseCase;
    private readonly IOpenFileLocationUseCase _openFileLocationUseCase;
    private readonly IGetLocalFilePathUseCase _getLocalFilePathUseCase;
    private readonly IGetDesktopConnectorStatusUseCase _getDesktopConnectorStatusUseCase;
    private readonly IGetSyncedProjectStatusUseCase _getSyncedProjectStatusUseCase;
    private readonly ICdeStructureUseCase _cdeStructureUseCase;
    private readonly ILogger<FilesViewModel> _logger;
    private readonly ColumnSelectorViewModel _columnSelectorViewModel;
    private readonly FileFilterViewModel _fileFilterViewModel;
    private readonly Stack<string> _navigationStack = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string?>> _customAttributesByItemUrn = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly DispatcherTimer _clipboardStatusTimer;

    // Cancels a still-running LoadFolderItemsAsync call when a newer one starts (e.g. the user
    // switched projects again before the first one finished) - see LoadFolderItemsAsync. Also
    // cancelled by CancelLoad (the user's own Cancel button) and by Dispose.
    private CancellationTokenSource? _loadFolderItemsCts;

    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<ItemDto> items = [];

    /// <summary>
    /// The view the DataGrid actually binds to. Wraps <see cref="Items"/> with the search/filter
    /// predicate from <see cref="FileFilterViewModel"/> applied - a separate property rather than
    /// filtering <see cref="Items"/> itself, since <see cref="ItemDto"/> is immutable and several
    /// existing code paths already replace or rebuild the raw collection wholesale (a full folder
    /// reload, custom-attribute enrichment, an auto-refresh tick); this view re-wraps whichever
    /// collection is current without those paths needing to know filtering exists.
    /// </summary>
    [ObservableProperty]
    private ICollectionView? itemsView;

    /// <summary>Column filters, search box, and clear-all for the files grid.</summary>
    public FileFilterViewModel FileFilterViewModel => _fileFilterViewModel;

    [ObservableProperty]
    private string? currentFolderName;

    [ObservableProperty]
    private string? currentHubId;

    [ObservableProperty]
    private string? currentProjectId;

    [ObservableProperty]
    private string? currentHubName;

    [ObservableProperty]
    private string? currentProjectName;

    [ObservableProperty]
    private string? currentFolderId;

    [ObservableProperty]
    private string? currentAccessToken;

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Human-readable status shown alongside the loading indicator (e.g. "Loading 12 of 40 files...")
    /// so a long project load doesn't look stalled behind a plain indeterminate spinner.
    /// </summary>
    [ObservableProperty]
    private string? loadingStatusText;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool canNavigateBack;

    [ObservableProperty]
    private ItemDto? selectedItem;

    [ObservableProperty]
    private ObservableCollection<MetadataFieldDisplayDto> availableMetadataFields = [];

    /// <summary>
    /// The naming standard's own filename-code fields (e.g. Project, Originator, Volume/System,
    /// Level/Location) - deliberately EXCLUDED from <see cref="AvailableMetadataFields"/> because
    /// they aren't independently editable through Edit Attributes (see
    /// <c>DataManagementService.GetProjectNamingStandardAsync</c>), but still carry a real per-file
    /// value worth showing as a grid column. Populated from <see cref="ICdeStructureUseCase.GetNamingConventionAsync"/>,
    /// the same cached source the deliverable wizard uses for the identical fields.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MetadataFieldDisplayDto> namingCodeFields = [];

    /// <summary>
    /// The full set of attribute fields the grid's columns and column-selector menu should offer -
    /// <see cref="AvailableMetadataFields"/> (true, editable custom attributes) plus
    /// <see cref="NamingCodeFields"/> (read-only filename-code fields), deduplicated by name so a
    /// naming standard that happens to define the same name in both sources doesn't produce two
    /// columns. Only the grid uses this union; Edit Attributes keeps using <see cref="AvailableMetadataFields"/>
    /// directly.
    /// </summary>
    public IReadOnlyList<MetadataFieldDisplayDto> GridMetadataFields =>
        AvailableMetadataFields
            .Concat(NamingCodeFields.Where(nc => !AvailableMetadataFields.Any(f => string.Equals(f.Name, nc.Name, StringComparison.OrdinalIgnoreCase))))
            .ToList();

    [ObservableProperty]
    private ObservableCollection<MetadataFieldValueDto> selectedItemMetadata = [];

    [ObservableProperty]
    private DesktopConnectorInfo? desktopConnectorInfo;

    [ObservableProperty]
    private bool isOpeningFile;

    /// <summary>
    /// Brief confirmation text shown after "Copy Path"/"Copy Link" succeed (e.g. "Path copied to
    /// clipboard"), auto-cleared by <see cref="_clipboardStatusTimer"/> a couple seconds later -
    /// see <see cref="ShowClipboardStatus"/>.
    /// </summary>
    [ObservableProperty]
    private string? clipboardStatusText;

    [ObservableProperty]
    private SyncedProject? syncedProjectStatus;

    // --- Background deliverable-creation progress ---
    //
    // The "New Deliverable" wizard dialog closes the instant the user clicks Create; the actual
    // batch (which can be several sequential HTTP round trips per document) runs afterward, owned by
    // MainWindow.NewDeliverableButton_Click, with nothing left to bind to once the dialog is gone.
    // These properties are that binding target, following the same pattern IsLoading/LoadingStatusText
    // already use for folder loading, so the rest of the app stays fully usable while a batch runs.

    private CancellationTokenSource? _deliverableCreationCts;

    [ObservableProperty]
    private bool isCreatingDeliverables;

    /// <summary>
    /// True from the moment Cancel is clicked until the batch actually stops. Cancellation only takes
    /// effect between items (see <c>CreateDeliverableUseCase.CreateManyAsync</c>), so a document
    /// already in progress still finishes - without this, the button would appear to do nothing for
    /// however long that takes.
    /// </summary>
    [ObservableProperty]
    private bool isCancellingDeliverableCreation;

    [ObservableProperty]
    private string? deliverableCreationStatusText;

    [ObservableProperty]
    private int deliverableCreationCompleted;

    [ObservableProperty]
    private int deliverableCreationTotal = 1;

    /// <summary>The batch's final summary, left visible until dismissed rather than auto-hidden.</summary>
    [ObservableProperty]
    private string? deliverableCreationResultMessage;

    /// <summary>True when the result should read as a warning (amber) rather than a clean success (green).</summary>
    [ObservableProperty]
    private bool deliverableCreationHadProblems;

    /// <summary>
    /// Starts tracking a new deliverable batch and returns the token the caller should pass through
    /// to <c>ICreateDeliverableUseCase.CreateManyAsync</c>. Cancels and replaces any token from a
    /// previous batch, though in practice a new batch cannot start while one is already running - see
    /// the IsCreatingDeliverables-bound IsEnabled on the "New Deliverable" button.
    /// </summary>
    public CancellationToken BeginDeliverableCreation(int total)
    {
        _deliverableCreationCts?.Cancel();
        _deliverableCreationCts?.Dispose();
        _deliverableCreationCts = new CancellationTokenSource();

        IsCreatingDeliverables = true;
        IsCancellingDeliverableCreation = false;
        DeliverableCreationCompleted = 0;
        DeliverableCreationTotal = total;
        DeliverableCreationStatusText = total == 1 ? "Creating deliverable..." : $"Creating 0 of {total} deliverables...";
        DeliverableCreationResultMessage = null;
        DeliverableCreationHadProblems = false;

        return _deliverableCreationCts.Token;
    }

    /// <summary>
    /// Called once per item as a batch progresses. Stops overwriting the status text once cancelling
    /// has started, so "Cancelling..." stays on screen instead of being replaced by one more
    /// "Creating N of M..." from the item that was already in flight when Cancel was clicked.
    /// </summary>
    public void ReportDeliverableCreationProgress(int completed, int total)
    {
        DeliverableCreationCompleted = completed;
        DeliverableCreationTotal = total;

        if (!IsCancellingDeliverableCreation)
        {
            DeliverableCreationStatusText = total == 1
                ? "Creating deliverable..."
                : $"Creating {completed} of {total} deliverables...";
        }
    }

    /// <summary>Called once the batch has finished (or been cancelled), with the summary to display.</summary>
    public void CompleteDeliverableCreation(string resultMessage, bool hadProblems)
    {
        IsCreatingDeliverables = false;
        IsCancellingDeliverableCreation = false;
        DeliverableCreationResultMessage = resultMessage;
        DeliverableCreationHadProblems = hadProblems;

        _deliverableCreationCts?.Dispose();
        _deliverableCreationCts = null;
    }

    [RelayCommand]
    private void CancelDeliverableCreation()
    {
        if (_deliverableCreationCts is null || IsCancellingDeliverableCreation)
        {
            return;
        }

        // Set immediately, before the token is even cancelled, so the button reacts on click rather
        // than only once the in-flight document finishes and the loop actually stops.
        IsCancellingDeliverableCreation = true;
        DeliverableCreationStatusText = "Cancelling...";

        _deliverableCreationCts.Cancel();
    }

    [RelayCommand]
    private void DismissDeliverableCreationResult() => DeliverableCreationResultMessage = null;

    /// <summary>
    /// Column visibility configuration for the file list.
    /// </summary>
    public ColumnSelectorViewModel ColumnSelectorViewModel => _columnSelectorViewModel;

    /// <summary>
    /// Computed property: Desktop Connector is available and installed.
    /// </summary>
    public bool IsDesktopConnectorAvailable => DesktopConnectorInfo?.IsInstalled ?? false;

    /// <summary>
    /// Computed property: the currently selected project is available locally (synced or syncing).
    /// </summary>
    public bool IsProjectAvailableLocally =>
        SyncedProjectStatus?.Status is SyncStatus.Synced;

    /// <summary>
    /// True once loading has finished and the current folder/project genuinely has no files, so the
    /// UI can show an explanatory empty-state message instead of a bare, unexplained blank grid.
    /// </summary>
    public bool ShowEmptyState => !IsLoading && Items.Count == 0;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));

        // A new load clears any "Cancelling..." state left over from the previous one.
        if (value)
        {
            IsCancellingLoad = false;
        }

        CancelLoadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// True from the moment the user clicks Cancel on a running load until it actually stops.
    ///
    /// <para>
    /// Mirrors the deliverable-batch pattern below. Cancellation is observed after each awaited APS
    /// call, so a request already in flight still completes first; without this flag the button
    /// would look like it had done nothing for however long that takes.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool isCancellingLoad;

    /// <summary>
    /// Abandons the project load in progress.
    ///
    /// <para>
    /// Loading a project is the longest operation in the application - a recursive folder walk, then
    /// a path lookup per file, then the attribute batch - and until now there was no way to stop it.
    /// A user who picked the wrong project could only wait for it to finish. The cancellation
    /// machinery already existed for the superseded-load case; this just exposes it.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelLoad))]
    private void CancelLoad()
    {
        if (!IsLoading || IsCancellingLoad)
        {
            return;
        }

        _logger.LogInformation("Project load cancelled by the user");

        IsCancellingLoad = true;
        LoadingStatusText = "Cancelling...";

        _loadFolderItemsCts?.Cancel();
    }

    private bool CanCancelLoad() => IsLoading;

    /// <summary>
    /// Rebuilds the filtered view whenever <see cref="Items"/> is replaced wholesale (a fresh folder
    /// load). The filter predicate itself never changes here - it always calls back into
    /// <see cref="FileFilterViewModel.Matches"/> - only which underlying collection it wraps does.
    /// </summary>
    partial void OnItemsChanged(ObservableCollection<ItemDto> value)
    {
        var view = CollectionViewSource.GetDefaultView(value);
        view.Filter = obj => obj is ItemDto item && _fileFilterViewModel.Matches(item);
        ItemsView = view;
    }

    /// <summary>Filter and column selections are scoped per project - see <see cref="FileFilterViewModel.SetProject"/> and <see cref="ColumnSelectorViewModel.SetProject"/>.</summary>
    partial void OnCurrentProjectIdChanged(string? value)
    {
        // Project-scoped schema drives the dynamic grid columns and selector entries. Clear it
        // immediately so fields from the previous project cannot appear while the new one loads.
        _loadFolderItemsCts?.Cancel();
        AvailableMetadataFields.Clear();
        NamingCodeFields.Clear();
        _fileFilterViewModel.SetProject(value);
        _columnSelectorViewModel.SetProject(value);
    }

    public FilesViewModel(
        IFileListingUseCase fileListingUseCase,
        IDataManagementService dataManagementService,
        IOpenFileLocallyUseCase openFileLocallyUseCase,
        IOpenFileLocationUseCase openFileLocationUseCase,
        IGetLocalFilePathUseCase getLocalFilePathUseCase,
        IGetDesktopConnectorStatusUseCase getDesktopConnectorStatusUseCase,
        IGetSyncedProjectStatusUseCase getSyncedProjectStatusUseCase,
        ICdeStructureUseCase cdeStructureUseCase,
        ColumnSelectorViewModel columnSelectorViewModel,
        FileFilterViewModel fileFilterViewModel,
        ILogger<FilesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(fileListingUseCase);
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(openFileLocallyUseCase);
        ArgumentNullException.ThrowIfNull(openFileLocationUseCase);
        ArgumentNullException.ThrowIfNull(getLocalFilePathUseCase);
        ArgumentNullException.ThrowIfNull(getDesktopConnectorStatusUseCase);
        ArgumentNullException.ThrowIfNull(getSyncedProjectStatusUseCase);
        ArgumentNullException.ThrowIfNull(cdeStructureUseCase);
        ArgumentNullException.ThrowIfNull(columnSelectorViewModel);
        ArgumentNullException.ThrowIfNull(fileFilterViewModel);
        ArgumentNullException.ThrowIfNull(logger);

        _fileListingUseCase = fileListingUseCase;
        _dataManagementService = dataManagementService;
        _openFileLocallyUseCase = openFileLocallyUseCase;
        _openFileLocationUseCase = openFileLocationUseCase;
        _getLocalFilePathUseCase = getLocalFilePathUseCase;
        _getDesktopConnectorStatusUseCase = getDesktopConnectorStatusUseCase;
        _getSyncedProjectStatusUseCase = getSyncedProjectStatusUseCase;
        _cdeStructureUseCase = cdeStructureUseCase;
        _columnSelectorViewModel = columnSelectorViewModel;
        _fileFilterViewModel = fileFilterViewModel;
        // Named handlers, not lambdas: Dispose has to detach these, and an anonymous handler cannot
        // be unsubscribed.
        _fileFilterViewModel.FiltersChanged += OnFiltersChanged;
        _logger = logger;
        CurrentFolderName = "Files";

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _autoRefreshTimer.Tick += AutoRefreshTimerTick;

        _clipboardStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _clipboardStatusTimer.Tick += ClipboardStatusTimerTick;

        // Initialize Desktop Connector status on load
        _ = RefreshDesktopConnectorStatusAsync();
    }

    /// <summary>
    /// Shows a brief confirmation message that auto-clears after a couple seconds - restarting the
    /// timer on every call so two copies in quick succession each get their own full display window
    /// rather than the second one being cut short by the first's timer.
    /// </summary>
    private void ShowClipboardStatus(string message)
    {
        ClipboardStatusText = message;
        _clipboardStatusTimer.Stop();
        _clipboardStatusTimer.Start();
    }

    /// <summary>
    /// Loads items from the current project.
    /// Set CurrentProjectId and CurrentAccessToken before calling.
    /// </summary>
    [RelayCommand]
    public async Task LoadFolderItemsAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentHubId) || string.IsNullOrWhiteSpace(CurrentProjectId) || string.IsNullOrWhiteSpace(CurrentAccessToken))
        {
            return;
        }

        // Cancel any load already in flight. Without this, switching projects again before the
        // first project's load finishes lets that earlier call resume (after its awaited API
        // call returns) with CurrentProjectId already pointing at the NEW project while it's
        // still holding the OLD project's Items/item URNs - sending a mismatched project id +
        // item URN combination to ACC's custom-attributes endpoint, which rejects it with 400.
        // Cancelling the superseded call's token and checking it after every await below stops
        // it from touching shared state (Items, AvailableMetadataFields) once it's stale.
        _loadFolderItemsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadFolderItemsCts = cts;
        var cancellationToken = cts.Token;

        var hubId = CurrentHubId!;
        var projectId = CurrentProjectId!;
        var accessToken = CurrentAccessToken!;

        IsLoading = true;
        IsCancellingLoad = false;
        ErrorMessage = null;
        LoadingStatusText = "Loading files...";

        try
        {
            _logger.LogInformation("Loading files for hub {HubId} and project {ProjectId}", hubId, projectId);

            var response = await _fileListingUseCase.GetAllProjectFilesAsync(hubId, projectId, accessToken, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Items = new ObservableCollection<ItemDto>(response.Items);

            CurrentFolderName = "Project Files";
            CanNavigateBack = false;
            _logger.LogInformation("Loaded {ItemCount} files", Items.Count);

            SelectedItem = null;
            SelectedItemMetadata.Clear();

            // CRITICAL: Enrich ALL items with path and web link metadata FIRST
            // This ensures WebViewLink is always available, not just for items with custom attributes
            await EnrichItemsWithPathMetadataAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // THEN enrich with custom attributes (which won't overwrite path/weblink data)
            LoadingStatusText = "Loading custom attributes...";
            await LoadCustomAttributesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await LoadProjectMetadataAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await LoadNamingCodeFieldsAsync(hubId, projectId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Rebuilds every column's distinct-value list from the now-fully-enriched items, then
            // re-applies whichever search/filter selection is current (freshly loaded from saved
            // preferences, on this project's first load).
            _fileFilterViewModel.Rebuild(Items, GridMetadataFields);
            ItemsView?.Refresh();

            if (!_autoRefreshTimer.IsEnabled)
            {
                _autoRefreshTimer.Start();
            }
        }
        catch (OperationCanceledException)
        {
            // Either superseded by a newer LoadFolderItemsAsync call (the user switched projects
            // again) or cancelled on purpose via CancelLoad. Neither is a failure, so neither is
            // surfaced as one.
            _logger.LogInformation("Load cancelled or superseded for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load files");
            ErrorMessage = $"Failed to load files: {ex.Message}";
        }
        finally
        {
            // Only the still-current load resets these - a stale, superseded call must not
            // clear the spinner/status text out from under the load that replaced it.
            if (ReferenceEquals(_loadFolderItemsCts, cts))
            {
                IsLoading = false;
                IsCancellingLoad = false;
                LoadingStatusText = null;

                cts.Dispose();
                _loadFolderItemsCts = null;
            }
        }
    }

    /// <summary>
    /// Adds files this application just created straight into the grid, instead of reloading the
    /// project.
    ///
    /// <para>
    /// Creating one deliverable used to be followed by a full <see cref="LoadFolderItemsAsync"/>:
    /// the entire folder tree re-walked, a path lookup re-issued for every pre-existing file, the
    /// attribute batch re-fetched for all of them, and the schema re-resolved - to surface one row
    /// that was already fully described by the creation response. For a 28-file project that was
    /// tens of requests and tens of seconds for one new file, and it is where most of the delay
    /// after clicking Create went.
    /// </para>
    ///
    /// <para>
    /// Everything the grid needs is already in hand. The created item carries its id, name, version
    /// urn and containing folder; its location comes from the folder it was created in; its
    /// attributes are the values just written to it. The one field not known locally is the ACC web
    /// view link, which needs that file's own path lookup - one request for the new file, rather
    /// than one for every file in the project.
    /// </para>
    /// </summary>
    /// <param name="createdItems">The items returned by the creation calls.</param>
    /// <param name="attributesByItemUrn">
    /// The attribute values written to each created item, keyed by item urn. Supplied by the caller
    /// because they are what it just sent to ACC - re-reading them back would be another request per
    /// file to learn what the application itself chose.
    /// </param>
    public async Task AddCreatedItemsAsync(
        IReadOnlyCollection<ItemDto> createdItems,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>? attributesByItemUrn = null)
    {
        ArgumentNullException.ThrowIfNull(createdItems);

        if (createdItems.Count == 0)
        {
            return;
        }

        var projectId = CurrentProjectId;

        foreach (var created in createdItems)
        {
            if (string.IsNullOrWhiteSpace(created.Id))
            {
                continue;
            }

            // Guard against a double-add: the auto-refresh tick or a manual Refresh may already have
            // picked the file up between creation and this call.
            if (Items.Any(i => string.Equals(i.Id, created.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var attributes = created.ItemUrn is not null && attributesByItemUrn is not null
                ? attributesByItemUrn.GetValueOrDefault(created.ItemUrn)
                : null;

            if (attributes is not null && created.ItemUrn is not null)
            {
                _customAttributesByItemUrn[created.ItemUrn] = attributes;
            }

            var item = created with
            {
                CustomAttributes = attributes ?? created.CustomAttributes,
                // Derived from the destination path exactly as the folder walk derives it for every
                // other row, so a created file sorts and filters identically to an existing one.
                Location = created.Location,
                LifecycleStage = created.LifecycleStage
            };

            Items.Add(item);
        }

        // One path lookup per created file, for the web view link. Failures are deliberately not
        // fatal: the row is already correct and usable without a link, and the next refresh fills it
        // in - the alternative (reloading everything to get it) is what this method exists to avoid.
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            foreach (var created in createdItems.Where(c => !string.IsNullOrWhiteSpace(c.ItemUrn)))
            {
                try
                {
                    var metadata = await _dataManagementService.GetItemPathMetadataAsync(
                        projectId!, created.ItemUrn!, CancellationToken.None);

                    if (metadata is null)
                    {
                        continue;
                    }

                    for (var i = 0; i < Items.Count; i++)
                    {
                        if (string.Equals(Items[i].Id, created.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            Items[i] = Items[i] with
                            {
                                PathInProject = metadata.PathInProject,
                                WebViewLink = metadata.WebViewLink
                            };
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex, "Could not resolve the path metadata for newly created '{FileName}'", created.Name);
                }
            }
        }

        _logger.LogInformation(
            "Added {Count} newly created file(s) to the grid without reloading the project", createdItems.Count);

        // New rows can introduce new values (a new Status, a new Revision), so the filter
        // dropdowns are rebuilt - the same thing a full reload did, minus the reload.
        _fileFilterViewModel.Rebuild(Items, GridMetadataFields);
        ItemsView?.Refresh();
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>
    /// Stops all background activity and clears project state, for sign-out.
    ///
    /// <para>
    /// Clearing <see cref="Items"/> alone (what logout used to do) left the five-minute auto-refresh
    /// timer running against a project the application no longer had a token for, producing a failed
    /// load and an error banner some minutes after the user signed out.
    /// </para>
    /// </summary>
    public void ResetForSignOut()
    {
        _autoRefreshTimer.Stop();
        _clipboardStatusTimer.Stop();

        _loadFolderItemsCts?.Cancel();

        Items.Clear();
        SelectedItem = null;
        SelectedItemMetadata.Clear();
        AvailableMetadataFields.Clear();
        NamingCodeFields.Clear();
        _customAttributesByItemUrn.Clear();

        CurrentHubId = null;
        CurrentHubName = null;
        CurrentProjectId = null;
        CurrentProjectName = null;
        CurrentFolderId = null;
        CurrentAccessToken = null;
        SyncedProjectStatus = null;
        ErrorMessage = null;
        ClipboardStatusText = null;
        LoadingStatusText = null;

        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>
    /// Stops the dispatcher timers and abandons any load still running.
    ///
    /// <para>
    /// A <see cref="DispatcherTimer"/> keeps a strong reference to its Tick handler and so to this
    /// ViewModel, and the dispatcher keeps a reference to the timer - so a running timer never
    /// becomes collectable and keeps firing for as long as the dispatcher lives. Nothing disposed
    /// this ViewModel before: WPF does not dispose a window's DataContext, and the DI container
    /// registers it for the application's lifetime. <c>App.OnExit</c> now does, via
    /// <see cref="MainViewModel.Dispose"/>.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= AutoRefreshTimerTick;

        _clipboardStatusTimer.Stop();
        _clipboardStatusTimer.Tick -= ClipboardStatusTimerTick;

        _fileFilterViewModel.FiltersChanged -= OnFiltersChanged;

        _loadFolderItemsCts?.Cancel();
        _loadFolderItemsCts?.Dispose();
        _loadFolderItemsCts = null;

        _deliverableCreationCts?.Cancel();
        _deliverableCreationCts?.Dispose();
        _deliverableCreationCts = null;
    }

    /// <summary>
    /// Enriches all items with path and web view link metadata from APS.
    /// This ensures WebViewLink is always available for all files, not just those with custom attributes.
    /// Calls GET /data/v1/projects/{projectId}/items/{itemUrn}?includePathInProject=true for each item.
    /// </summary>
    private async Task EnrichItemsWithPathMetadataAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(CurrentProjectId))
        {
            return;
        }

        // Safe to read CurrentProjectId here: if a newer LoadFolderItemsAsync call had started,
        // it would have cancelled this token first (see LoadFolderItemsAsync), so an
        // uncancelled token means no project switch has happened since this load began.
        var projectId = CurrentProjectId!;

        // Only enrich file items (folders don't have web links or meaningful paths)
        var fileItems = Items.Where(item => item.Type == ItemType.File && !string.IsNullOrWhiteSpace(item.ItemUrn)).ToList();

        if (fileItems.Count == 0)
        {
            _logger.LogInformation("No file items to enrich with path metadata");
            return;
        }

        _logger.LogInformation(
            "Enriching {FileCount} file items with path and web view link metadata",
            fileItems.Count);

        var totalCount = fileItems.Count;
        var completedCount = 0;
        LoadingStatusText = $"Loading 0 of {totalCount} files...";

        // Bounded concurrency. The comment here used to claim "with reasonable throttling" while
        // actually starting one task per file at once - 200 simultaneous requests for a 200-file
        // project, which is what provoked the 429s this fan-out now rides out via the retry handler.
        // Eight matches the limit the service layer's own bulk path lookup uses.
        const int MaxConcurrentRequests = 8;
        using var throttler = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);

        try
        {
            var tasks = fileItems.Select(async item =>
            {
                await throttler.WaitAsync(cancellationToken);

                try
                {
                    var metadata = await _dataManagementService.GetItemPathMetadataAsync(
                        projectId,
                        item.ItemUrn!,
                        cancellationToken);

                    if (metadata != null)
                    {
                        return item with
                        {
                            PathInProject = metadata.PathInProject,
                            WebViewLink = metadata.WebViewLink
                        };
                    }

                    _logger.LogWarning(
                        "Failed to retrieve path metadata for item {ItemName} (URN: {ItemUrn})",
                        item.Name,
                        item.ItemUrn);

                    return item;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error fetching path metadata for item {ItemName} (URN: {ItemUrn})",
                        item.Name,
                        item.ItemUrn);

                    // Return original item on error
                    return item;
                }
                finally
                {
                    throttler.Release();

                    var completed = Interlocked.Increment(ref completedCount);

                    // Left alone once the user has asked to cancel, so "Cancelling..." is not
                    // overwritten by progress from the requests already in flight.
                    if (!IsCancellingLoad)
                    {
                        LoadingStatusText = $"Loading {completed} of {totalCount} files...";
                    }
                }
            });

            var enrichedItems = await Task.WhenAll(tasks);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Update the observable collection
            // Build a dictionary of enriched items by ID for quick lookup
            var enrichedById = enrichedItems.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

            // Update items in place
            for (int i = 0; i < Items.Count; i++)
            {
                if (enrichedById.TryGetValue(Items[i].Id, out var enrichedItem))
                {
                    Items[i] = enrichedItem;
                }
            }

            var successCount = enrichedItems.Count(item => !string.IsNullOrWhiteSpace(item.WebViewLink));
            _logger.LogInformation(
                "Successfully enriched {SuccessCount}/{TotalCount} items with path metadata",
                successCount,
                fileItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enrich items with path metadata");
            // Don't throw - this is not critical enough to block file listing
        }
    }

    /// <summary>
    /// Manual refresh command for reloading project files.
    ///
    /// <para>
    /// Drops this project's cached metadata first, which is what makes an explicit Refresh mean
    /// "go and ask APS again" rather than "re-read what you already had". Caching the project's
    /// schema and its files' paths is what removed the repeated traffic; without this, a change made
    /// in ACC's web UI would not appear until the cached entry expired, and Refresh would look
    /// broken.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentProjectId))
        {
            _dataManagementService.InvalidateProjectCache(CurrentProjectId!);
            _cdeStructureUseCase.Invalidate(CurrentProjectId!);
        }

        await LoadFolderItemsAsync();
    }

    /// <summary>
    /// Refreshes Desktop Connector installation status.
    /// Called on initialization and can be called manually to refresh status.
    /// </summary>
    [RelayCommand]
    public async Task RefreshDesktopConnectorStatusAsync()
    {
        _logger.LogInformation("Refreshing Desktop Connector status");

        try
        {
            var info = await _getDesktopConnectorStatusUseCase.ExecuteAsync(CancellationToken.None);
            DesktopConnectorInfo = info;

            if (info.IsInstalled)
            {
                _logger.LogInformation(
                    "Desktop Connector detected: Version {Version}",
                    info.Version ?? "Unknown");
            }
            else
            {
                _logger.LogInformation("Desktop Connector not installed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Desktop Connector status");
            DesktopConnectorInfo = new DesktopConnectorInfo(false, null, null, null);
        }

        _ = RefreshSyncStatusAsync();
    }

    /// <summary>
    /// Checks whether the currently selected project is synced locally via Desktop Connector.
    /// No-op if no project is selected or Desktop Connector is not installed.
    /// </summary>
    [RelayCommand]
    public async Task RefreshSyncStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentHubName) || string.IsNullOrWhiteSpace(CurrentProjectName))
        {
            SyncedProjectStatus = null;
            return;
        }

        if (DesktopConnectorInfo?.IsInstalled != true)
        {
            SyncedProjectStatus = null;
            return;
        }

        try
        {
            SyncedProjectStatus = await _getSyncedProjectStatusUseCase.ExecuteAsync(
                CurrentHubName!, CurrentProjectName!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Desktop Connector project sync status");
            SyncedProjectStatus = new SyncedProject(CurrentHubName!, CurrentProjectName!, string.Empty, null, SyncStatus.Error);
        }
    }

    /// <summary>
    /// Attempts to open the selected file locally using Desktop Connector.
    /// Shows an error dialog if the file cannot be opened locally.
    /// </summary>
    [RelayCommand]
    public async Task OpenFileLocallyAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(CurrentHubName) || string.IsNullOrWhiteSpace(CurrentProjectName))
        {
            _logger.LogWarning("OpenFileLocallyAsync: No file selected or missing hub/project names");
            return;
        }

        IsOpeningFile = true;

        try
        {
            _logger.LogInformation(
                "Attempting to open file '{FileName}' locally",
                SelectedItem.Name);

            var result = await _openFileLocallyUseCase.ExecuteAsync(
                SelectedItem,
                CurrentHubName,
                CurrentProjectName,
                CancellationToken.None);

            if (result.Success)
            {
                _logger.LogInformation(
                    "File opened successfully locally: {LocalPath}",
                    result.LocalPath);
                ErrorMessage = null;
            }
            else
            {
                // Show error dialog with the failure reason
                _logger.LogWarning(
                    "Local file open failed: {ErrorMessage}",
                    result.ErrorMessage);
                
                ErrorMessage = result.ErrorMessage ?? "Unable to open file locally.";
                
                // Show error dialog to user
                ShowFileOpenErrorDialog(result.ErrorMessage, SelectedItem.WebViewLink);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attempting to open file locally");
            ErrorMessage = $"Error opening file: {ex.Message}";
            ShowFileOpenErrorDialog($"Error opening file: {ex.Message}", SelectedItem?.WebViewLink);
        }
        finally
        {
            IsOpeningFile = false;
        }
    }

    /// <summary>
    /// Resolves the selected file's local path via Desktop Connector and opens its containing
    /// folder in Windows Explorer with the file selected. Shows an error dialog if the file's
    /// location cannot be resolved locally.
    /// </summary>
    [RelayCommand]
    public async Task OpenFileLocationAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(CurrentHubName) || string.IsNullOrWhiteSpace(CurrentProjectName))
        {
            _logger.LogWarning("OpenFileLocationAsync: No file selected or missing hub/project names");
            return;
        }

        IsOpeningFile = true;

        try
        {
            _logger.LogInformation(
                "Attempting to open file location for '{FileName}'",
                SelectedItem.Name);

            var result = await _openFileLocationUseCase.ExecuteAsync(
                SelectedItem,
                CurrentHubName,
                CurrentProjectName,
                CancellationToken.None);

            if (result.Success)
            {
                _logger.LogInformation(
                    "File location opened successfully: {LocalPath}",
                    result.LocalPath);
                ErrorMessage = null;
            }
            else
            {
                _logger.LogWarning(
                    "Open file location failed: {ErrorMessage}",
                    result.ErrorMessage);

                ErrorMessage = result.ErrorMessage ?? "Unable to open file location.";
                ShowFileOpenErrorDialog(result.ErrorMessage, SelectedItem.WebViewLink);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attempting to open file location");
            ErrorMessage = $"Error opening file location: {ex.Message}";
            ShowFileOpenErrorDialog($"Error opening file location: {ex.Message}", SelectedItem?.WebViewLink);
        }
        finally
        {
            IsOpeningFile = false;
        }
    }

    /// <summary>
    /// Resolves the selected file's local Desktop Connector path and copies it to the clipboard,
    /// without opening anything - the read-only counterpart to <see cref="OpenFileLocationAsync"/>.
    /// Only meaningful when the current project is synced locally (see <see cref="IsProjectAvailableLocally"/>),
    /// which the context menu already gates on before this command is ever invoked.
    /// </summary>
    [RelayCommand]
    public async Task CopyPathAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(CurrentHubName) || string.IsNullOrWhiteSpace(CurrentProjectName))
        {
            _logger.LogWarning("CopyPathAsync: No file selected or missing hub/project names");
            return;
        }

        try
        {
            var result = await _getLocalFilePathUseCase.ExecuteAsync(
                SelectedItem,
                CurrentHubName,
                CurrentProjectName,
                CancellationToken.None);

            if (result.Success && !string.IsNullOrWhiteSpace(result.LocalPath))
            {
                Clipboard.SetText(result.LocalPath);
                ShowClipboardStatus("Path copied to clipboard");
                _logger.LogInformation("Copied local path for '{FileName}' to clipboard", SelectedItem.Name);
            }
            else
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "Unable to resolve the local file path.",
                    "Copy Path",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying local file path");
            MessageBox.Show($"Error copying path: {ex.Message}", "Copy Path", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Copies the selected file's ACC web view link to the clipboard - always available, unlike
    /// <see cref="CopyPathAsync"/>, since it doesn't depend on Desktop Connector or local sync.
    /// </summary>
    [RelayCommand]
    public void CopyLink()
    {
        if (SelectedItem?.WebViewLink is null)
        {
            MessageBox.Show(
                "Web view link is not available for this item.",
                "Link Not Available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(SelectedItem.WebViewLink);
        ShowClipboardStatus("Link copied to clipboard");
        _logger.LogInformation("Copied web view link for '{FileName}' to clipboard", SelectedItem.Name);
    }

    /// <summary>
    /// Shows an error dialog when file opening fails.
    /// Includes option to view file on web if link is available.
    /// </summary>
    private void ShowFileOpenErrorDialog(string errorMessage, string? webViewLink)
    {
        var messageBoxText = errorMessage ?? "Unable to open file locally.";
        var caption = "File Open Error";
        var buttons = MessageBoxButton.OK;
        var icon = MessageBoxImage.Warning;

        // If web view link is available, offer option to open there
        if (!string.IsNullOrWhiteSpace(webViewLink))
        {
            buttons = MessageBoxButton.OKCancel;
            messageBoxText += "\n\nDo you want to view the file on the web instead?";
        }

        var result = MessageBox.Show(messageBoxText, caption, buttons, icon);

        if (result == MessageBoxResult.Cancel && !string.IsNullOrWhiteSpace(webViewLink))
        {
            TryOpenWebViewUrl(webViewLink);
        }
    }

    /// <summary>
    /// Attempts to open a URL in the default browser.
    /// </summary>
    private void TryOpenWebViewUrl(string url)
    {
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open web view URL: {Url}", url);
        }
    }

    private void OnFiltersChanged() => ItemsView?.Refresh();

    private void ClipboardStatusTimerTick(object? sender, EventArgs e)
    {
        _clipboardStatusTimer.Stop();
        ClipboardStatusText = null;
    }

    private async void AutoRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_disposed
            || IsLoading
            || IsCreatingDeliverables
            || string.IsNullOrWhiteSpace(CurrentHubId)
            || string.IsNullOrWhiteSpace(CurrentProjectId)
            || string.IsNullOrWhiteSpace(CurrentAccessToken))
        {
            // Skipping while a deliverable batch runs is new and deliberate: an auto-refresh in the
            // middle of a batch re-reads the folder the batch is actively writing to, competing with
            // it for the rate limit for a listing that is out of date the moment the next file
            // lands.
            return;
        }

        // Does NOT clear the cache, unlike the user-driven RefreshAsync below. An automatic tick is
        // there to pick up new and changed FILES; a project's attribute schema and its files' paths
        // are not what it is looking for, and dropping them would make every tick pay the full
        // uncached cost for data that had not changed.
        await LoadFolderItemsAsync();
        _ = RefreshSyncStatusAsync();
    }

    private async Task LoadCustomAttributesAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation(
            "Loading custom attributes for project {ProjectId} folder {FolderId}",
            CurrentProjectId,
            CurrentFolderId);

        try
        {
            // Signal that custom attributes are loading
            _columnSelectorViewModel.IsCustomAttributesLoading = true;
            _columnSelectorViewModel.ClearCustomAttributeVisibility();

            _customAttributesByItemUrn.Clear();

            var itemUrns = Items
                .Select(item => item.ItemUrn)
                .Where(urn => !string.IsNullOrWhiteSpace(urn))
                .Select(urn => urn!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (itemUrns.Count == 0 || string.IsNullOrWhiteSpace(CurrentProjectId))
            {
                AvailableMetadataFields.Clear();
                return;
            }

            // Safe to read CurrentProjectId here: if a newer LoadFolderItemsAsync call had
            // started, it would have cancelled this token first, and we already returned above.
            var projectId = CurrentProjectId!;

            _logger.LogInformation(
                "Fetching custom attributes for {ItemCount} items in project {ProjectId}",
                itemUrns.Count,
                projectId);

            // includePathMetadata: false - paths and web links are resolved by the separate pass
            // above (EnrichItemsWithPathMetadataAsync), which covers every file rather than only the
            // ones this batch returns a row for. Asking for them here as well would duplicate that
            // work: one extra HTTP request per file, for values this method then discards.
            var results = await _dataManagementService.GetItemCustomAttributesAsync(
                projectId,
                itemUrns,
                includePathMetadata: false,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var resultList = results.ToList();

            // Create a lookup dictionary for quick access by ItemUrn
            var resultsByUrn = resultList.ToDictionary(
                r => r.Urn,
                r => r,
                StringComparer.OrdinalIgnoreCase);

            if (resultsByUrn.Count < itemUrns.Count)
            {
                _logger.LogWarning(
                    "Custom attribute enrichment incomplete. Requested {RequestedCount} items, received {ReceivedCount}.",
                    itemUrns.Count,
                    resultsByUrn.Count);
            }

            // Enrich items with custom attributes
            var enrichedItems = Items
                .Select(item =>
                {
                    // Store in cache for detail panel
                    if (!string.IsNullOrWhiteSpace(item.ItemUrn) && resultsByUrn.TryGetValue(item.ItemUrn, out var result))
                    {
                        var attributes = new Dictionary<string, string?>(result.Attributes, StringComparer.OrdinalIgnoreCase);
                        _customAttributesByItemUrn[item.ItemUrn] = attributes;

                        // Only update custom attributes - preserve PathInProject and WebViewLink from Step 1 enrichment
                        // The batch-get endpoint doesn't return weblinks, so we must not overwrite them
                        return item with
                        {
                            CustomAttributes = result.Attributes,
                            TipVersionUrn = result.VersionUrn
                        };
                    }

                    return item;
                })
                .ToList();

            // Update observable collection with enriched items
            Items.Clear();
            foreach (var item in enrichedItems)
            {
                Items.Add(item);
            }

            if (SelectedItem is not null)
            {
                PopulateSelectedItemMetadata(SelectedItem);
            }

            _logger.LogInformation(
                "Completed custom attribute enrichment for {EnrichedCount} items in project {ProjectId}",
                resultsByUrn.Count,
                CurrentProjectId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load custom attributes for project {ProjectId} folder {FolderId}",
                CurrentProjectId,
                CurrentFolderId);
            throw;
        }
        finally
        {
            // Signal that custom attributes are done loading
            _columnSelectorViewModel.IsCustomAttributesLoading = false;
        }
    }

    /// <summary>
    /// Loads metadata field definitions (attribute IDs, types, and options) for the project's true
    /// custom attributes - i.e. exactly what ACC web's own "Edit Attributes" modal would show, with
    /// naming-standard/file-naming-scheme fields already excluded by the service layer. There is
    /// intentionally no fallback that derives fields from observed attribute names: doing so would
    /// re-include naming-scheme fields (Project, Originator, etc.), since those still carry real
    /// per-file values even though they aren't independently editable.
    /// </summary>
    [RelayCommand]
    public async Task LoadProjectMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentHubId) || string.IsNullOrWhiteSpace(CurrentProjectId))
        {
            AvailableMetadataFields.Clear();
            return;
        }

        var hubId = CurrentHubId;
        var projectId = CurrentProjectId;
        try
        {
            var namingStandard = await _dataManagementService.GetProjectNamingStandardAsync(
                hubId,
                projectId,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested
                || !string.Equals(CurrentHubId, hubId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(CurrentProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Do not exclude a real Title field: it belongs in Edit Attributes and is offered
            // as a dynamic grid column. Only Deliverable Title is reserved by the static column.
            AvailableMetadataFields.Clear();
            foreach (var field in namingStandard.Metadata)
            {
                AvailableMetadataFields.Add(field);
            }

            _logger.LogInformation(
                "AvailableMetadataFields populated with {Count} fields: [{Fields}]",
                AvailableMetadataFields.Count,
                string.Join(", ", AvailableMetadataFields.Select(f => $"{f.Name}(id={f.AttributeId})")));

            if (SelectedItem is not null)
            {
                PopulateSelectedItemMetadata(SelectedItem);
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested
                && string.Equals(CurrentHubId, hubId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(CurrentProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Failed to load custom attribute schema for project {ProjectId}", projectId);
                AvailableMetadataFields.Clear();
            }
        }
    }

    /// <summary>
    /// Loads the project's naming-code fields (Project, Originator, Volume/System, Level/Location,
    /// and similar) so they can appear as read-only grid columns - see <see cref="NamingCodeFields"/>
    /// for why these are sourced separately from <see cref="AvailableMetadataFields"/>. A project with
    /// no naming standard configured simply gets none of these columns, which is not an error.
    /// </summary>
    private async Task LoadNamingCodeFieldsAsync(string hubId, string projectId, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var convention = await _cdeStructureUseCase.GetNamingConventionAsync(hubId, projectId, cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !string.Equals(CurrentHubId, hubId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(CurrentProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            NamingCodeFields.Clear();
            foreach (var field in convention.Fields)
            {
                NamingCodeFields.Add(new MetadataFieldDisplayDto(
                    Name: field.Name,
                    Type: field.Type,
                    Description: null,
                    Optional: field.Optional,
                    Options: field.Options,
                    DefaultValue: field.DefaultValue,
                    AttributeId: field.AttributeId));
            }

            _logger.LogInformation(
                "NamingCodeFields populated with {Count} field(s) for project {ProjectId}: [{Fields}]",
                NamingCodeFields.Count,
                projectId,
                string.Join(", ", NamingCodeFields.Select(f => f.Name)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested
                && string.Equals(CurrentHubId, hubId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(CurrentProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Failed to load naming-code fields for project {ProjectId}", projectId);
                NamingCodeFields.Clear();
            }
        }
    }

    /// <summary>
    /// Handles selection of an item. Only allows files to be selected.
    /// Displays metadata for the selected file.
    /// </summary>
    partial void OnSelectedItemChanged(ItemDto? oldValue, ItemDto? newValue)
    {
        // Only allow selection of files, not folders
        if (newValue is not null && newValue.Type == ItemType.Folder)
        {
            _logger.LogWarning("Attempted to select folder '{FolderName}'. Only files can be selected.", newValue.Name);
            // Deselect the folder
            SelectedItem = oldValue;
            return;
        }

        if (newValue is not null)
        {
            PopulateSelectedItemMetadata(newValue);
        }
        else
        {
            SelectedItemMetadata.Clear();
        }
    }

    /// <summary>
    /// Populates the metadata display for the selected item.
    /// </summary>
    private void PopulateSelectedItemMetadata(ItemDto selectedItem)
    {
        SelectedItemMetadata.Clear();

        foreach (var field in AvailableMetadataFields)
        {
            var displayValue = GetMetadataValue(selectedItem, field.Name) ?? field.DefaultValue;
            if (displayValue is null)
            {
                continue;
            }

            SelectedItemMetadata.Add(new MetadataFieldValueDto(
                FieldName: field.Name,
                FieldType: field.Type,
                DisplayValue: displayValue,
                FieldDescription: field.Description
            ));
        }
    }

    /// <summary>
    /// Retrieves the metadata value for an item field.
    /// Cache can be populated when batch-fetching custom attributes.
    /// </summary>
    private string? GetMetadataValue(ItemDto item, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(item.ItemUrn))
        {
            return null;
        }

        if (_customAttributesByItemUrn.TryGetValue(item.ItemUrn, out var attributes)
            && attributes.TryGetValue(fieldName, out var value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// Merges freshly-saved custom attribute values (from the Edit Attributes dialog) into the
    /// local cache and grid so the change is visible immediately without a full folder reload.
    /// </summary>
    public void ApplyUpdatedCustomAttributes(string itemUrn, IReadOnlyDictionary<string, string?> updatedValues)
    {
        if (string.IsNullOrWhiteSpace(itemUrn))
        {
            return;
        }

        var merged = _customAttributesByItemUrn.TryGetValue(itemUrn, out var existing)
            ? new Dictionary<string, string?>(existing, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in updatedValues)
        {
            merged[name] = value;
        }

        _customAttributesByItemUrn[itemUrn] = merged;

        for (int i = 0; i < Items.Count; i++)
        {
            if (string.Equals(Items[i].ItemUrn, itemUrn, StringComparison.OrdinalIgnoreCase))
            {
                Items[i] = Items[i] with { CustomAttributes = merged };
            }
        }

        if (SelectedItem is not null && string.Equals(SelectedItem.ItemUrn, itemUrn, StringComparison.OrdinalIgnoreCase))
        {
            PopulateSelectedItemMetadata(SelectedItem);
        }

        // A value edited here (e.g. a new Status) has to appear as an option in that column's filter
        // dropdown immediately - Edit Attributes doesn't go through a full LoadFolderItemsAsync, so
        // without this the filter's distinct-value lists would only catch up on the next folder
        // reload or the 5-minute auto-refresh, and a value the user just set would look like it
        // "doesn't exist" in its own filter.
        _fileFilterViewModel.Rebuild(Items, GridMetadataFields);
        ItemsView?.Refresh();
    }
}