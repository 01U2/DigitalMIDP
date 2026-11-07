using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.ViewModels;
using DigitalMIDP.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Specialized;

namespace DigitalMIDP;

/// <summary>
/// Main application window.
/// Minimal code-behind - only for view initialization and event routing to ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private readonly Dictionary<string, DataGridColumn> _dynamicColumns = new();
    private bool _isSessionLocked;

    public MainWindow()
    {
        InitializeComponent();

        // Resolve and set MainViewModel from DI container
        _viewModel = App.Services.GetService<MainViewModel>();
        DataContext = _viewModel;

        // Add handlers to dynamically add custom attribute columns
        Loaded += MainWindow_Loaded;

        // Auto-refresh is paused while nobody can see the grid - see FilesViewModel.SetAutoRefreshPaused.
        // SystemEvents is static, so the handler is detached in OnClosed or it would keep this window alive.
        StateChanged += (_, _) => UpdateAutoRefreshPause();
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        base.OnClosed(e);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                _isSessionLocked = true;
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                _isSessionLocked = false;
                break;
            default:
                return;
        }

        // SessionSwitch is raised on a SystemEvents thread; the ViewModel and its timer belong to the UI thread.
        Dispatcher.BeginInvoke(UpdateAutoRefreshPause);
    }

    private void UpdateAutoRefreshPause() =>
        _viewModel?.FilesViewModel.SetAutoRefreshPaused(WindowState == WindowState.Minimized || _isSessionLocked);

    /// <summary>
    /// Initializes dynamic DataGrid columns for custom attributes.
    /// Subscribes to metadata field changes and refreshes columns.
    /// </summary>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FilesViewModel == null)
            return;

        // Subscribe to column visibility changes
        if (_viewModel.FilesViewModel.ColumnSelectorViewModel != null)
        {
            _viewModel.FilesViewModel.ColumnSelectorViewModel.PropertyChanged += (s, args) =>
            {
                UpdateColumnVisibility();
            };
        }

        // Subscribe to metadata fields collection changes
        _viewModel.FilesViewModel.AvailableMetadataFields.CollectionChanged +=
            (s, args) => Dispatcher.Invoke(() =>
            {
                RefreshCustomAttributeColumns();
                RefreshColumnSelectorMenu();
                AttachColumnFilterHeaders();
            });

        // Naming-code fields (Project, Originator, Volume/System, Level/Location, ...) rebuild the
        // same columns/menu, independently of AvailableMetadataFields - see FilesViewModel.GridMetadataFields.
        _viewModel.FilesViewModel.NamingCodeFields.CollectionChanged +=
            (s, args) => Dispatcher.Invoke(() =>
            {
                RefreshCustomAttributeColumns();
                RefreshColumnSelectorMenu();
                AttachColumnFilterHeaders();
            });

        // Subscribe to items collection changes to refresh columns if needed
        _viewModel.FilesViewModel.Items.CollectionChanged +=
            (s, args) =>
            {
                if (args.NewItems?.Count > 0)
                {
                    RefreshCustomAttributeColumns();
                    RefreshColumnSelectorMenu();
                    AttachColumnFilterHeaders();
                }
            };

        // FileFilterViewModel.Rebuild() (in FilesViewModel.LoadFolderItemsAsync) replaces
        // ColumnFilters independently of the two subscriptions above, and can run either before or
        // after them depending on load timing - re-attaching headers here as well, rather than
        // relying on one fixed order, is what makes a dynamic column's filter dropdown show up
        // reliably regardless of which finishes first.
        _viewModel.FilesViewModel.FileFilterViewModel.ColumnFilters.CollectionChanged +=
            (s, args) => Dispatcher.Invoke(AttachColumnFilterHeaders);

        // Initial refresh
        RefreshCustomAttributeColumns();
        RefreshColumnSelectorMenu();
        UpdateColumnVisibility();
        AttachColumnFilterHeaders();
    }

    /// <summary>
    /// Assigns each filterable column's Header (an <see cref="AttributeFilterViewModel"/>) and shared
    /// HeaderTemplate, for both the static columns (Name, Title, Stage, Status, Revision, Path in Project) and every dynamic custom
    /// attribute column. Safe to call repeatedly and in any order relative to the column/filter
    /// rebuilds that trigger it - a column whose matching filter doesn't exist yet is simply left
    /// alone until the next call finds it.
    /// </summary>
    private void AttachColumnFilterHeaders()
    {
        var filterViewModel = _viewModel?.FilesViewModel?.FileFilterViewModel;
        if (filterViewModel is null)
        {
            return;
        }

        var headerTemplate = (DataTemplate)FindResource("FilterableColumnHeaderTemplate");

        if (filterViewModel.NameFilter is not null && FindName("NameColumn") is DataGridColumn nameColumn)
        {
            nameColumn.Header = filterViewModel.NameFilter;
            nameColumn.HeaderTemplate = headerTemplate;
        }

        if (filterViewModel.TitleFilter is not null && TitleColumn is not null)
        {
            TitleColumn.Header = filterViewModel.TitleFilter;
            TitleColumn.HeaderTemplate = headerTemplate;
        }

        if (filterViewModel.StageFilter is not null && StageColumn is not null)
        {
            StageColumn.Header = filterViewModel.StageFilter;
            StageColumn.HeaderTemplate = headerTemplate;
        }

        if (filterViewModel.StatusFilter is not null && FindName("StatusColumn") is DataGridColumn statusColumn)
        {
            statusColumn.Header = filterViewModel.StatusFilter;
            statusColumn.HeaderTemplate = headerTemplate;
        }

        if (filterViewModel.RevisionFilter is not null && FindName("RevisionColumn") is DataGridColumn revisionColumn)
        {
            revisionColumn.Header = filterViewModel.RevisionFilter;
            revisionColumn.HeaderTemplate = headerTemplate;
        }

        if (filterViewModel.PathInProjectFilter is not null && PathInProjectColumn is not null)
        {
            PathInProjectColumn.Header = filterViewModel.PathInProjectFilter;
            PathInProjectColumn.HeaderTemplate = headerTemplate;
        }

        foreach (var (fieldName, column) in _dynamicColumns)
        {
            var columnFilter = filterViewModel.ColumnFilters.FirstOrDefault(f =>
                string.Equals(f.ColumnKey, fieldName, StringComparison.OrdinalIgnoreCase));

            if (columnFilter is not null)
            {
                column.Header = columnFilter;
                column.HeaderTemplate = headerTemplate;
            }
        }
    }

    /// <summary>
    /// True for an attribute name that already has a dedicated static column (Deliverable Title, Stage, Status, Revision) - used to
    /// skip building a redundant dynamic column or menu item for a project whose naming standard
    /// happens to define a real custom attribute by one of those names. Deliberately not applied to
    /// <c>AvailableMetadataFields</c> itself, since Edit Attributes needs the real entry present to
    /// let the user edit it - only the grid's own column/menu construction should skip it.
    /// </summary>
    private static bool IsReservedColumnName(string name) =>
        string.Equals(name, ColumnKeys.DeliverableTitle, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, ColumnKeys.Stage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, ColumnKeys.Status, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, ColumnKeys.Revision, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refreshes DataGrid columns based on available metadata fields.
    /// Removes old custom attribute columns and adds new ones.
    /// </summary>
    private void RefreshCustomAttributeColumns()
    {
        if (_viewModel?.FilesViewModel?.AvailableMetadataFields == null)
            return;

        var dataGrid = FindName("FilesDataGrid") as DataGrid;
        if (dataGrid == null)
            return;

        var columnSelector = _viewModel.FilesViewModel.ColumnSelectorViewModel;

        // Remove existing custom attribute columns (keep standard columns: Name, Title, Stage, Status, Revision, Path in Project)
        const int StandardColumnCount = 6; // Name, Title, Stage, Status, Revision, Path in Project
        while (dataGrid.Columns.Count > StandardColumnCount)
        {
            dataGrid.Columns.RemoveAt(StandardColumnCount);
        }

        // Clear dynamic columns tracking
        _dynamicColumns.Clear();

        // Add columns for every attribute field the grid should offer - both true custom attributes
        // and the naming-code fields (Project, Originator, Volume/System, Level/Location, ...) that
        // Edit Attributes deliberately excludes but which still carry a real per-file value.
        foreach (var field in _viewModel.FilesViewModel.GridMetadataFields)
        {
            // "Deliverable Title" and "Stage" already have a dedicated static column reading this exact data, so
            // a project whose naming standard also defines a real attribute by either name would
            // otherwise get it twice here - once as the static column, once as an identical-looking
            // dynamic one. AvailableMetadataFields itself is left unfiltered (see
            // FilesViewModel.LoadProjectMetadataAsync) because Edit Attributes needs the real "Deliverable Title"
            // entry to let the user edit it - only the grid's own column/menu building skip it.
            if (IsReservedColumnName(field.Name))
            {
                continue;
            }

            // Decides the default the FIRST time this column is seen for the current project (Revision
            // and Status default visible, everything else - including every naming-code column -
            // defaults hidden); a no-op once a value already exists, whether from a saved preference
            // or the user's own toggle this session, so this refresh never clobbers either.
            columnSelector.EnsureCustomAttributeColumnDefaultVisibility(field.Name);

            // Serialize options to JSON for the converter to use for description lookup
            // Format: "FieldName|[{"Value":"code","Description":"label"}]"
            var optionsJson = SerializeOptions(field.Options);
            var converterParameter = string.IsNullOrWhiteSpace(optionsJson)
                ? field.Name
                : $"{field.Name}|{optionsJson}";

            var binding = new Binding("CustomAttributes")
            {
                Converter = new Converters.CustomAttributeValueConverter(),
                ConverterParameter = converterParameter,
                Mode = BindingMode.OneWay
            };

            // Use DataGridTemplateColumn to match default column formatting
            var column = new DataGridTemplateColumn
            {
                Header = field.Name,
                Width = new DataGridLength(150, DataGridLengthUnitType.Pixel),
                MinWidth = 120,
                CellTemplate = new DataTemplate(),
                Visibility = columnSelector.GetCustomAttributeColumnVisibility(field.Name)
                    ? Visibility.Visible
                    : Visibility.Collapsed
            };

            // Create a FrameworkElementFactory for TextBlock with vertical alignment and left margin
            var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            textBlockFactory.SetBinding(TextBlock.TextProperty, binding);
            textBlockFactory.SetBinding(TextBlock.ToolTipProperty, binding);
            textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textBlockFactory.SetValue(TextBlock.MarginProperty, new Thickness(5, 0, 0, 0));

            column.CellTemplate.VisualTree = textBlockFactory;

            dataGrid.Columns.Add(column);
            _dynamicColumns[field.Name] = column;
        }
    }

    /// <summary>
    /// Updates the visibility of all DataGrid columns based on ColumnSelectorViewModel.
    /// </summary>
    private void UpdateColumnVisibility()
    {
        if (_viewModel?.FilesViewModel?.ColumnSelectorViewModel == null)
            return;

        var dataGrid = FindName("FilesDataGrid") as DataGrid;
        if (dataGrid == null || dataGrid.Columns.Count < 6)
            return;

        var columnSelector = _viewModel.FilesViewModel.ColumnSelectorViewModel;

        // Update standard columns visibility
        // Layout: Name, Title, Stage, Status, Revision, Path in Project
        dataGrid.Columns[0].Visibility = columnSelector.IsNameColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        dataGrid.Columns[1].Visibility = columnSelector.IsTypeColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        dataGrid.Columns[2].Visibility = columnSelector.IsStageColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        dataGrid.Columns[3].Visibility = columnSelector.IsStatusColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        dataGrid.Columns[4].Visibility = columnSelector.IsRevisionColumnVisible ? Visibility.Visible : Visibility.Collapsed;
        dataGrid.Columns[5].Visibility = columnSelector.IsLocationColumnVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Refreshes the column selector context menu to include custom attribute columns.
    /// Shows a loading indicator while custom attributes are being fetched from APS.
    /// </summary>
    private void RefreshColumnSelectorMenu()
    {
        if (_viewModel?.FilesViewModel?.AvailableMetadataFields == null)
            return;

        var menu = ColumnSelectorMenu;
        if (menu == null)
            return;

        // Remove existing custom attribute menu items (keep first items for header and 6 standard columns)
        // Standard menu: Name, Title, Stage, Status, Revision, Path in Project (with checks and separators)
        const int StandardMenuItemCount = 8; // Header, 6 columns with checks, separator
        while (menu.Items.Count > StandardMenuItemCount)
        {
            menu.Items.RemoveAt(StandardMenuItemCount);
        }

        // Show separator if there are custom attributes or loading is in progress
        var separator = FindName("CustomAttributesSeparator") as Separator;
        if (separator != null)
        {
            var isLoading = _viewModel.FilesViewModel.ColumnSelectorViewModel.IsCustomAttributesLoading;
            var hasAttributes = _viewModel.FilesViewModel.AvailableMetadataFields.Count > 0;
            separator.Visibility = (hasAttributes || isLoading) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Show loading indicator if custom attributes are being fetched
        if (_viewModel.FilesViewModel.ColumnSelectorViewModel.IsCustomAttributesLoading)
        {
            var loadingMenuItem = new MenuItem
            {
                Header = "Custom Attributes Loading...",
                IsEnabled = false,
                StaysOpenOnClick = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            menu.Items.Add(loadingMenuItem);
            return; // Don't add custom attribute items yet
        }

        // Add menu items for every attribute field the grid offers (custom attributes plus
        // naming-code fields - see FilesViewModel.GridMetadataFields).
        foreach (var field in _viewModel.FilesViewModel.GridMetadataFields)
        {
            if (IsReservedColumnName(field.Name))
            {
                continue;
            }

            var isChecked = _viewModel.FilesViewModel.ColumnSelectorViewModel.GetCustomAttributeColumnVisibility(field.Name);

            var menuItem = new MenuItem
            {
                Header = field.Name,
                IsCheckable = true,
                IsChecked = isChecked,
                StaysOpenOnClick = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // Handle checkbox changes
            menuItem.Click += (s, e) =>
            {
                if (_dynamicColumns.TryGetValue(field.Name, out var column))
                {
                    column.Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    _viewModel.FilesViewModel.ColumnSelectorViewModel.SetCustomAttributeColumnVisibility(field.Name, menuItem.IsChecked);
                }
            };

            menu.Items.Add(menuItem);
        }
    }

    /// <summary>
    /// Handles the Columns button click to show the column selector menu.
    /// </summary>
    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// Handles double-click on the DataGrid to open selected file locally.
    /// Ignores double-clicks on folders.
    /// </summary>
    private void FilesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.FilesViewModel?.SelectedItem == null)
        {
            return;
        }

        var selectedItem = _viewModel.FilesViewModel.SelectedItem;

        // Ignore double-clicks on folders
        if (selectedItem.Type == ItemType.Folder)
        {
            e.Handled = true;
            return;
        }

        // Only proceed if Desktop Connector is available
        if (!_viewModel.FilesViewModel.IsDesktopConnectorAvailable)
        {
            MessageBox.Show(
                "Desktop Connector is not installed. Please install it to open files locally.",
                "Desktop Connector Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            e.Handled = true;
            return;
        }

        // Invoke the open file command
        if (_viewModel.FilesViewModel.OpenFileLocallyCommand?.CanExecute(null) ?? false)
        {
            _viewModel.FilesViewModel.OpenFileLocallyCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Governs whether the context menu shows at all, and which items it offers, based on the
    /// current selection: no selection → don't show the menu at all (right-clicking empty space
    /// shouldn't offer file actions); a single file selected → all actions are relevant; multiple
    /// files selected → only "Edit Attributes..." makes sense as a batch action, so "View on Web"
    /// and "Open File Location" (both inherently single-file operations) are hidden. "Open File
    /// Location" additionally requires the current project to actually be synced locally via
    /// Desktop Connector - there's no local path to open otherwise.
    /// </summary>
    private void FilesDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var selectedCount = (sender as DataGrid)?.SelectedItems.Count ?? 0;

        if (selectedCount == 0)
        {
            e.Handled = true;
            return;
        }

        var isSingleFileSelected = selectedCount == 1;
        var isProjectSynced = _viewModel?.FilesViewModel?.IsProjectAvailableLocally ?? false;

        ViewOnWebMenuItem.Visibility = isSingleFileSelected ? Visibility.Visible : Visibility.Collapsed;
        OpenFileLocationMenuItem.Visibility = isSingleFileSelected && isProjectSynced ? Visibility.Visible : Visibility.Collapsed;

        // Copy Path needs a resolved local Desktop Connector path, so it only makes sense for a
        // synced project - Copy Link has no such dependency, so it's always available.
        CopyPathMenuItem.Visibility = isSingleFileSelected && isProjectSynced ? Visibility.Visible : Visibility.Collapsed;
        CopyLinkMenuItem.Visibility = isSingleFileSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Explorer-style deselection: clicking on empty space in the grid (below the last row, or in an
    /// unused column area) clears the current selection entirely, instead of leaving it selected with
    /// no visible row to show for it. A click that actually lands on a row is left alone so normal
    /// row selection/multi-select behavior is untouched.
    /// </summary>
    private void FilesDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hitElement = e.OriginalSource as DependencyObject;
        while (hitElement != null && hitElement is not DataGridRow)
        {
            hitElement = VisualTreeHelper.GetParent(hitElement);
        }

        if (hitElement == null && sender is DataGrid dataGrid)
        {
            dataGrid.UnselectAll();
            dataGrid.SelectedItem = null;
        }
    }

    /// <summary>
    /// Preserves an existing multi-selection when right-clicking within it to open the context menu.
    /// WPF's default DataGridRow mouse-down behavior collapses the selection to just the clicked row
    /// unless Ctrl/Shift is held - that's fine for a left-click, but breaks "select several files,
    /// then right-click one of them" for a context menu. If the clicked row is already part of the
    /// current selection, suppress the default handling so the multi-selection (and its highlighting)
    /// stays intact; a right-click on a row outside the current selection still selects just that row.
    /// </summary>
    private void FilesDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { IsSelected: true })
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles right-click "Open File Location" context menu option.
    /// Resolves the selected item's local path via Desktop Connector and opens its containing
    /// folder in Windows Explorer with the file selected.
    /// </summary>
    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FilesViewModel?.OpenFileLocationCommand?.CanExecute(null) ?? false)
        {
            _viewModel.FilesViewModel.OpenFileLocationCommand.Execute(null);
        }
    }

    /// <summary>
    /// Handles right-click "Copy Path" context menu option. Only shown for a synced project - see
    /// <see cref="FilesDataGrid_ContextMenuOpening"/>.
    /// </summary>
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FilesViewModel?.CopyPathCommand?.CanExecute(null) ?? false)
        {
            _viewModel.FilesViewModel.CopyPathCommand.Execute(null);
        }
    }

    /// <summary>
    /// Handles right-click "Copy Link" context menu option.
    /// </summary>
    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FilesViewModel?.CopyLinkCommand?.CanExecute(null) ?? false)
        {
            _viewModel.FilesViewModel.CopyLinkCommand.Execute(null);
        }
    }

    /// <summary>
    /// Handles right-click "View on Web" context menu option.
    /// Opens the selected item's web view link in the default browser.
    /// </summary>
    private void ViewOnWeb_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FilesViewModel?.SelectedItem?.WebViewLink == null)
        {
            MessageBox.Show(
                "Web view link is not available for this item.",
                "Link Not Available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _viewModel.FilesViewModel.SelectedItem.WebViewLink,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to open web view: {ex.Message}",
                "Error Opening Web View",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Handles right-click "Edit Attributes..." context menu option.
    /// Opens a modal dialog to edit ACC custom attribute values for the selected file(s).
    /// Supports both a single selection and a multi-select batch edit.
    /// </summary>
    private void EditAttributes_Click(object sender, RoutedEventArgs e)
    {
        var filesViewModel = _viewModel?.FilesViewModel;
        if (filesViewModel == null)
        {
            return;
        }

        var dataGrid = FindName("FilesDataGrid") as DataGrid;
        var selectedItems = dataGrid?.SelectedItems.Cast<ItemDto>().ToList() ?? [];

        if (selectedItems.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filesViewModel.CurrentProjectId)
            || selectedItems.Any(i => string.IsNullOrWhiteSpace(i.ItemUrn)))
        {
            MessageBox.Show(
                "Unable to edit attributes: project or file information is missing.",
                "Edit Attributes",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // The dialog's field list is the project-wide custom attribute set (AvailableMetadataFields,
        // already fetched once when the folder loaded, and already excluding filename-code fields).
        // The same fields apply to every file regardless of which folder it's in - confirmed with the
        // client that subfolder-scoped custom attributes aren't a real scenario here - so there's no
        // need for a live per-click fetch (or a multi-file intersection) like there used to be.
        var availableFields = filesViewModel.AvailableMetadataFields.ToList();

        var editViewModel = App.Services.GetRequiredService<EditCustomAttributesViewModel>();
        editViewModel.Initialize(
            filesViewModel.CurrentProjectId!,
            selectedItems,
            availableFields);

        editViewModel.AttributesUpdated += updatedValuesByItemUrn =>
        {
            foreach (var (itemUrn, values) in updatedValuesByItemUrn)
            {
                filesViewModel.ApplyUpdatedCustomAttributes(itemUrn, values);
            }
        };

        var dialog = new EditCustomAttributesWindow(editViewModel) { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Opens the New Deliverable wizard. The wizard is initialized before it is shown so a project
    /// that cannot support creation (no naming standard, no WIP folder, no templates) reports the
    /// reason on its first screen rather than failing part-way through.
    /// </summary>
    private async void NewDeliverableButton_Click(object sender, RoutedEventArgs e)
    {
        var filesViewModel = _viewModel?.FilesViewModel;
        if (filesViewModel == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filesViewModel.CurrentHubId)
            || string.IsNullOrWhiteSpace(filesViewModel.CurrentProjectId))
        {
            MessageBox.Show(
                "Select a hub and project before creating a deliverable.",
                "New Deliverable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Defensive: the button is bound IsEnabled="{Binding ...IsCreatingDeliverables, Converter=...}"
        // so this should not be reachable mid-batch, but a second guard here costs nothing and avoids
        // two batches racing each other's document-number allocation in the same discipline.
        if (filesViewModel.IsCreatingDeliverables)
        {
            MessageBox.Show(
                "A batch of deliverables is already being created. Wait for it to finish before starting another.",
                "New Deliverable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var createViewModel = App.Services.GetRequiredService<CreateDeliverableViewModel>();

        // The wizard closes the instant Create is clicked (see CreateDeliverableWindow's
        // constructor), well before anything has actually been created - this is how the request it
        // built is retrieved afterward, rather than waiting on a return value from ShowDialog().
        CreateDeliverableBatchRequestDto? batchRequest = null;
        createViewModel.CreationRequested += request => batchRequest = request;

        var dialog = new CreateDeliverableWindow(createViewModel) { Owner = this };

        // Loading the project's naming convention/folders/templates costs several HTTP round trips.
        // The dialog must not wait on that before it appears - it shows immediately, already carrying
        // its own busy indicator (IsBusy/BusyMessage, bound in the XAML), and initialization runs once
        // the window is actually on screen rather than blocking this click handler beforehand.
        dialog.Loaded += async (_, _) =>
        {
            await createViewModel.InitializeAsync(
                filesViewModel.CurrentHubId!,
                filesViewModel.CurrentProjectId!);
        };

        dialog.ShowDialog();

        // A null request means the user cancelled rather than confirming a batch - nothing to run.
        if (batchRequest is not null)
        {
            await RunDeliverableBatchAsync(filesViewModel, batchRequest);
        }
    }

    /// <summary>
    /// Runs a deliverable batch to completion, reporting progress through
    /// <see cref="FilesViewModel"/>'s deliverable-creation properties so the status strip in
    /// MainWindow's XAML can show it. Awaited by its caller (so this click handler does not return
    /// until the batch is done), but that await does not block the UI thread - WPF keeps pumping
    /// messages across an await, which is what lets the rest of the window stay interactive while
    /// this runs, exactly like every other awaited call in this codebase.
    /// </summary>
    private async Task RunDeliverableBatchAsync(FilesViewModel filesViewModel, CreateDeliverableBatchRequestDto request)
    {
        var total = request.PerItemTitleValues.Count;
        var cancellationToken = filesViewModel.BeginDeliverableCreation(total);

        var progress = new Progress<DeliverableBatchProgressDto>(p =>
            filesViewModel.ReportDeliverableCreationProgress(p.Completed, p.Total));

        try
        {
            // The wizard builds the request without a token (it never holds one); a current token is
            // attached only here, for the duration of the call.
            var tokenProvider = App.Services.GetRequiredService<ITokenProvider>();
            request = request with { AccessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken) };

            var createDeliverableUseCase = App.Services.GetRequiredService<ICreateDeliverableUseCase>();
            var result = await createDeliverableUseCase.CreateManyAsync(request, progress, cancellationToken);

            var (message, hadProblems) = FormatBatchResult(result, total);
            filesViewModel.CompleteDeliverableCreation(message, hadProblems);

            if (result.AnySucceeded)
            {
                // Adds the created rows directly instead of reloading the project.
                //
                // This used to be a full LoadFolderItemsAsync: the whole folder tree re-walked, a
                // path request re-issued for every pre-existing file, the attribute batch re-fetched
                // for all of them and the schema re-resolved - all to display files whose id, name,
                // folder and attributes the creation response had just handed us. For one new file in
                // a 28-file project that was the bulk of the wait after clicking Create.
                var createdItems = result.Items
                    .Where(i => i.Success && i.CreatedItem is not null)
                    .Select(i => i.CreatedItem!)
                    .ToList();

                await filesViewModel.AddCreatedItemsAsync(createdItems);
            }
        }
        catch (Exception ex)
        {
            filesViewModel.CompleteDeliverableCreation(
                $"Creating deliverables failed: {ex.Message}", hadProblems: true);
        }
    }

    /// <summary>
    /// Builds the one-line (or few-line) summary shown in the status strip once a batch finishes.
    /// Mirrors the wording the wizard itself used to show inline before creation moved to the
    /// background - see docs/file-creation-implementation-walkthrough.md for the original reasoning
    /// behind each case (single item vs batch, partial success, complete failure).
    /// </summary>
    private static (string Message, bool HadProblems) FormatBatchResult(
        CreateDeliverableBatchResultDto result, int requestedCount)
    {
        if (result.Items.Count == 0)
        {
            return ("The batch was cancelled before any document was created.", true);
        }

        var succeeded = result.Items.Where(i => i.Success).ToList();
        var failed = result.Items.Where(i => !i.Success).ToList();
        var cancelledEarly = result.Items.Count < requestedCount;

        if (succeeded.Count == 0)
        {
            var reasons = string.Join(" ", failed.Select(f => f.ErrorMessage));
            return (result.Items.Count == 1
                ? $"Could not create the deliverable: {reasons}"
                : $"None of the {result.Items.Count} documents could be created. {reasons}", true);
        }

        // A single document is the common case and reads as one flowing sentence - the file name is
        // not worth repeating a second time right after the headline already named it.
        if (result.Items.Count == 1)
        {
            var only = succeeded[0];
            var caveats = FormatItemCaveats(only);
            var headline = $"Created {only.FileName} in WIP/{only.DestinationFolder}.";

            return (
                string.IsNullOrEmpty(caveats) ? headline : $"{headline} {caveats}",
                !only.AttributesApplied);
        }

        var points = new List<string>
        {
            failed.Count == 0
                ? $"Created all {succeeded.Count} documents in WIP/{succeeded[0].DestinationFolder}."
                : $"Created {succeeded.Count} of {result.Items.Count} documents in WIP/{succeeded[0].DestinationFolder}."
        };

        // Per-item caveats are named by file so a problem with one document out of several is
        // traceable rather than an anonymous "some attributes failed" lost among the successes.
        points.AddRange(
            succeeded
                .Where(s => s.Warnings.Count > 0)
                .Select(s => $"{s.FileName}: {FormatItemCaveats(s)}"));

        if (failed.Count > 0)
        {
            points.Add($"Not created: {string.Join("; ", failed.Select(f => f.ErrorMessage))}");
        }

        if (cancelledEarly)
        {
            points.Add($"Cancelled after {result.Items.Count} of {requestedCount} documents.");
        }

        var hadProblems = failed.Count > 0 || cancelledEarly || succeeded.Any(s => !s.AttributesApplied);

        var message = points.Count == 1
            ? points[0]
            : string.Join(Environment.NewLine, points.Select(p => $"- {p}"));

        return (message, hadProblems);
    }

    /// <summary>
    /// Formats one succeeded item's non-fatal caveats. This is just <c>result.Warnings</c> joined -
    /// every path that sets <c>AttributesApplied = false</c> in CreateDeliverableUseCase already adds
    /// its own specific warning (e.g. "Classification couldn't be saved..."), so a separate generic
    /// "some attributes failed" line here would only repeat the same fact in vaguer words.
    /// </summary>
    private static string FormatItemCaveats(CreateDeliverableResultDto result) =>
        string.Join(" ", result.Warnings);

    /// <summary>
    /// Serializes MetadataFieldDisplayDto options to JSON string for passing to the converter.
    /// Format: [{"Value":"code","Description":"label"},...]
    /// Used to enable the CustomAttributeValueConverter to look up option descriptions.
    /// </summary>
    private static string? SerializeOptions(IReadOnlyList<DigitalMIDP.Application.Dtos.MetadataOptionDisplayDto> options)
    {
        if (options is null || options.Count == 0)
        {
            return null;
        }

        try
        {
            // Simple JSON serialization - can be replaced with System.Text.Json if added to dependencies
            var items = new System.Collections.Generic.List<string>();
            foreach (var option in options)
            {
                if (option.Value is not null)
                {
                    var value = option.Value.Replace("\"", "\\\"");
                    var description = (option.Description ?? string.Empty).Replace("\"", "\\\"");
                    items.Add($@"{{""Value"":""{value}"",""Description"":""{description}""}}");
                }
            }

            return items.Count > 0 ? $"[{string.Join(",", items)}]" : null;
        }
        catch
        {
            // If serialization fails, gracefully return null and fall back to raw values
            return null;
        }
    }
}
