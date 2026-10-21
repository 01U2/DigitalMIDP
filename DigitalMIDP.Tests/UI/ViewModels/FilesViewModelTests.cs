using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.Services;
using DigitalMIDP.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.ObjectModel;
using System.Reflection;
using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

/// <summary>
/// Unit tests for FilesViewModel, focusing on metadata loading and custom attribute display.
/// </summary>
public sealed class FilesViewModelTests
{
    private readonly IDataManagementService _dataManagementService;
    private readonly FileListingUseCase _fileListingUseCase;
    private readonly IOpenFileLocallyUseCase _openFileLocallyUseCase;
    private readonly IOpenFileLocationUseCase _openFileLocationUseCase;
    private readonly IGetLocalFilePathUseCase _getLocalFilePathUseCase;
    private readonly IGetDesktopConnectorStatusUseCase _getDesktopConnectorStatusUseCase;
    private readonly IGetSyncedProjectStatusUseCase _getSyncedProjectStatusUseCase;
    private readonly ICdeStructureUseCase _cdeStructureUseCase;
    private readonly ColumnSelectorViewModel _columnSelectorViewModel;
    private readonly FileFilterViewModel _fileFilterViewModel;
    private readonly ILogger<FilesViewModel> _logger;

    public FilesViewModelTests()
    {
        _dataManagementService = Substitute.For<IDataManagementService>();
        _fileListingUseCase = new FileListingUseCase(
            _dataManagementService, Substitute.For<ILogger<FileListingUseCase>>());
        _openFileLocallyUseCase = Substitute.For<IOpenFileLocallyUseCase>();
        _openFileLocationUseCase = Substitute.For<IOpenFileLocationUseCase>();
        _getLocalFilePathUseCase = Substitute.For<IGetLocalFilePathUseCase>();
        _getDesktopConnectorStatusUseCase = Substitute.For<IGetDesktopConnectorStatusUseCase>();
        _getSyncedProjectStatusUseCase = Substitute.For<IGetSyncedProjectStatusUseCase>();
        _cdeStructureUseCase = Substitute.For<ICdeStructureUseCase>();
        _columnSelectorViewModel = new ColumnSelectorViewModel();
        _fileFilterViewModel = new FileFilterViewModel(
            new FilterPreferencesStore(Substitute.For<ILogger<FilterPreferencesStore>>()),
            Substitute.For<ILogger<FileFilterViewModel>>());
        _logger = Substitute.For<ILogger<FilesViewModel>>();
    }

    private FilesViewModel CreateViewModel() => new(
        _fileListingUseCase,
        _dataManagementService,
        TokenProvider(),
        _openFileLocallyUseCase,
        _openFileLocationUseCase,
        _getLocalFilePathUseCase,
        _getDesktopConnectorStatusUseCase,
        _getSyncedProjectStatusUseCase,
        _cdeStructureUseCase,
        _columnSelectorViewModel,
        _fileFilterViewModel,
        _logger);

    private static Task LoadCustomAttributesAsync(FilesViewModel viewModel)
    {
        var method = typeof(FilesViewModel).GetMethod(
            "LoadCustomAttributesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Task)method!.Invoke(viewModel, new object[] { CancellationToken.None })!;
    }

    #region Metadata Loading Tests

    [Fact]
    public void ChangingProject_ClearsPriorProjectTitleFromGridMetadata()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = "b.old";
        viewModel.AvailableMetadataFields.Add(new MetadataFieldDisplayDto("Title", "string", null, true, [], null, 1));
        viewModel.NamingCodeFields.Add(new MetadataFieldDisplayDto("Old Code", "string", null, true, [], null, 2));
        viewModel.GridMetadataFields.Select(f => f.Name).Should().Contain("Title");

        viewModel.CurrentProjectId = "b.new";

        viewModel.AvailableMetadataFields.Should().BeEmpty();
        viewModel.NamingCodeFields.Should().BeEmpty();
        viewModel.GridMetadataFields.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadProjectMetadataAsync_StaleResponse_DoesNotReintroduceOldTitle()
    {
        var oldResponse = new TaskCompletionSource<ProjectNamingStandardDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dataManagementService.GetProjectNamingStandardAsync("hub", "b.old", Arg.Any<CancellationToken>())
            .Returns(_ => oldResponse.Task);
        _dataManagementService.GetProjectNamingStandardAsync("hub", "b.new", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProjectNamingStandardDto("new", "New", [])));
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub";
        viewModel.CurrentProjectId = "b.old";

        var pending = viewModel.LoadProjectMetadataAsync();
        viewModel.CurrentProjectId = "b.new";
        await viewModel.LoadProjectMetadataAsync();
        oldResponse.SetResult(new ProjectNamingStandardDto("old", "Old",
            [new MetadataFieldDisplayDto("Title", "string", null, true, [], null, 1)]));
        await pending;

        viewModel.GridMetadataFields.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadProjectMetadataAsync_CurrentProjectWithTitle_KeepsDynamicTitleField()
    {
        _dataManagementService.GetProjectNamingStandardAsync("hub", "b.current", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ProjectNamingStandardDto("current", "Current",
                [new MetadataFieldDisplayDto("Title", "string", null, true, [], null, 1)])));
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub";
        viewModel.CurrentProjectId = "b.current";

        await viewModel.LoadProjectMetadataAsync();

        viewModel.GridMetadataFields.Select(f => f.Name).Should().ContainSingle().Which.Should().Be("Title");
    }

    [Fact]
    public async Task LoadCustomAttributesAsync_WithCustomAttributes_UpdatesAvailableMetadataFields()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";

        var items = new ObservableCollection<ItemDto>
        {
            new ItemDto
            {
                Id = "itemId",
                Name = "File1.rvt",
                Type = ItemType.File,
                ItemUrn = itemUrn
            }
        };

        var customAttributes = new[]
        {
            new ItemCustomAttributesResultDto(
                Urn: itemUrn,
                Name: "File1.rvt",
                Title: "File1.rvt",
                LastModifiedTime: null,
                LastModifiedUserName: null,
                RevisionNumber: null,
                ApprovalStatus: null,
                PathInProject: null,
                WebViewLink: null,
                Attributes: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Category", "Structural" },
                    { "Status", "Draft" }
                }
            )
        };

        _dataManagementService.GetItemCustomAttributesAsync(projectId, Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<ItemCustomAttributesResultDto>>(customAttributes));

        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = projectId;
        viewModel.Items = items;

        // Act
        await LoadCustomAttributesAsync(viewModel);

        // Assert
        viewModel.Items[0].CustomAttributes.Should().NotBeNull();
        viewModel.Items[0].CustomAttributes!.Should().ContainKey("Category").WhoseValue.Should().Be("Structural");
    }

    [Fact]
    public async Task LoadProjectMetadataAsync_WithNoCachedAttributes_ClearsAvailableMetadataFields()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.AvailableMetadataFields = new ObservableCollection<MetadataFieldDisplayDto>
        {
            new MetadataFieldDisplayDto(
                Name: "Category",
                Type: "string",
                Description: null,
                Optional: true,
                Options: new List<MetadataOptionDisplayDto>(),
                DefaultValue: null)
        };

        // Act
        await viewModel.LoadProjectMetadataAsync();

        // Assert
        viewModel.AvailableMetadataFields.Should().BeEmpty();
    }

    #endregion

    #region Custom Attributes Loading Tests

    [Fact]
    public async Task LoadCustomAttributesAsync_WithValidItemUrns_EnrichesItemsWithAttributes()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var itemUrn2 = "urn:adsk.bim360:dm.item:itemId2:dm.version:versionId2";

        var items = new ObservableCollection<ItemDto>
        {
            new ItemDto
            {
                Id = "itemId",
                Name = "File1.rvt",
                Type = ItemType.File,
                ItemUrn = itemUrn
            },
            new ItemDto
            {
                Id = "itemId2",
                Name = "File2.rvt",
                Type = ItemType.File,
                ItemUrn = itemUrn2
            }
        };

        var customAttributes = new[]
        {
            new ItemCustomAttributesResultDto(
                Urn: itemUrn,
                Name: "File1.rvt",
                Title: "File1.rvt",
                LastModifiedTime: null,
                LastModifiedUserName: null,
                RevisionNumber: null,
                ApprovalStatus: null,
                PathInProject: "Project Files/Folder/File1.rvt",
                WebViewLink: "https://example.test/webview/1",
                Attributes: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Category", "Structural" },
                    { "Status", "Draft" }
                }
            ),
            new ItemCustomAttributesResultDto(
                Urn: itemUrn2,
                Name: "File2.rvt",
                Title: "File2.rvt",
                LastModifiedTime: null,
                LastModifiedUserName: null,
                RevisionNumber: null,
                ApprovalStatus: null,
                PathInProject: "Project Files/Folder/File2.rvt",
                WebViewLink: "https://example.test/webview/2",
                Attributes: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Category", "MEP" }
                }
            )
        };

        _dataManagementService.GetItemCustomAttributesAsync(projectId, Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<ItemCustomAttributesResultDto>>(customAttributes));

        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = projectId;
        viewModel.Items = items;

        // Act
        await LoadCustomAttributesAsync(viewModel);

        // Assert
        // Verify items were enriched with custom attributes
        viewModel.Items[0].CustomAttributes.Should().NotBeNull();
        viewModel.Items[0].CustomAttributes!.Should().ContainKey("Category").WhoseValue.Should().Be("Structural");
        viewModel.Items[0].CustomAttributes!.Should().ContainKey("Status").WhoseValue.Should().Be("Draft");

        viewModel.Items[1].CustomAttributes.Should().NotBeNull();
        viewModel.Items[1].CustomAttributes!.Should().ContainKey("Category").WhoseValue.Should().Be("MEP");
    }

    [Fact]
    public async Task LoadCustomAttributesAsync_WithNoItems_DoesNothing()
    {
        // Arrange
        var projectId = "b.projectId123";
        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = projectId;
        viewModel.Items = new ObservableCollection<ItemDto>();

        // Act
        await LoadCustomAttributesAsync(viewModel);

        // Assert
        await _dataManagementService.DidNotReceive()
            .GetItemCustomAttributesAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadCustomAttributesAsync_WithoutProjectId_DoesNothing()
    {
        // Arrange
        var items = new ObservableCollection<ItemDto>
        {
            new ItemDto
            {
                Id = "itemId",
                Name = "File1.rvt",
                Type = ItemType.File,
                ItemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId"
            }
        };

        var viewModel = CreateViewModel();
        viewModel.Items = items;

        // Act
        await LoadCustomAttributesAsync(viewModel);

        // Assert
        await _dataManagementService.DidNotReceive()
            .GetItemCustomAttributesAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadCustomAttributesAsync_WithItemsWithoutUrns_FiltersThemOut()
    {
        // Arrange
        var projectId = "b.projectId123";
        var validItemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";

        var items = new ObservableCollection<ItemDto>
        {
            new ItemDto
            {
                Id = "folderId",
                Name = "Subfolder",
                Type = ItemType.Folder,
                ItemUrn = null // Folders don't have URNs
            },
            new ItemDto
            {
                Id = "itemId",
                Name = "File.rvt",
                Type = ItemType.File,
                ItemUrn = validItemUrn
            }
        };

        var customAttributes = new[]
        {
            new ItemCustomAttributesResultDto(
                Urn: validItemUrn,
                Name: "File.rvt",
                Title: "File.rvt",
                LastModifiedTime: null,
                LastModifiedUserName: null,
                RevisionNumber: null,
                ApprovalStatus: null,
                PathInProject: null,
                WebViewLink: null,
                Attributes: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Category", "Structural" }
                }
            )
        };

        _dataManagementService.GetItemCustomAttributesAsync(projectId, Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<ItemCustomAttributesResultDto>>(customAttributes));

        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = projectId;
        viewModel.Items = items;

        // Act
        await LoadCustomAttributesAsync(viewModel);

        // Assert
        // Should only request attributes for the file, not the folder
        await _dataManagementService.Received(1)
            .GetItemCustomAttributesAsync(
                projectId,
                Arg.Is<IEnumerable<string>>(urns => urns.Count() == 1 && urns.First() == validItemUrn),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());

        // Verify folder still has no attributes
        viewModel.Items[0].CustomAttributes.Should().BeNull();

        // Verify file has attributes
        viewModel.Items[1].CustomAttributes.Should().NotBeNull();
    }

    #endregion

    #region Selected Item Metadata Tests

    [Fact]
    public void PopulateSelectedItemMetadata_WithSelectedItem_DisplaysMetadata()
    {
        // Arrange
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var selectedItem = new ItemDto
        {
            Id = "itemId",
            Name = "File.rvt",
            Type = ItemType.File,
            ItemUrn = itemUrn
        };

        var customAttributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            { "Category", "Structural" },
            { "Status", "Active" }
        };

        var metadataFields = new ObservableCollection<MetadataFieldDisplayDto>
        {
            new MetadataFieldDisplayDto(
                Name: "Category",
                Type: "string",
                Description: "Item category",
                Optional: false,
                Options: new List<MetadataOptionDisplayDto>(),
                DefaultValue: null
            ),
            new MetadataFieldDisplayDto(
                Name: "Status",
                Type: "string",
                Description: "Item status",
                Optional: true,
                Options: new List<MetadataOptionDisplayDto>(),
                DefaultValue: null
            ),
            new MetadataFieldDisplayDto(
                Name: "Unused",
                Type: "string",
                Description: "Unused field",
                Optional: true,
                Options: new List<MetadataOptionDisplayDto>(),
                DefaultValue: null
            )
        };

        var viewModel = CreateViewModel();
        viewModel.AvailableMetadataFields = metadataFields;
        viewModel.SelectedItem = selectedItem;

        // Add attributes to internal cache (normally populated by LoadCustomAttributesAsync)
        var attributeCache = typeof(FilesViewModel)
            .GetField("_customAttributesByItemUrn", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(viewModel) as Dictionary<string, IReadOnlyDictionary<string, string?>>;

        if (attributeCache != null)
        {
            attributeCache[itemUrn] = customAttributes;
        }

        // Act
        var populateMethod = typeof(FilesViewModel)
            .GetMethod("PopulateSelectedItemMetadata", BindingFlags.NonPublic | BindingFlags.Instance);
        populateMethod?.Invoke(viewModel, new object?[] { selectedItem });

        // Assert
        viewModel.SelectedItemMetadata.Should().HaveCount(2);
        viewModel.SelectedItemMetadata.Should()
            .Contain(m => m.FieldName == "Category" && m.DisplayValue == "Structural");
        viewModel.SelectedItemMetadata.Should()
            .Contain(m => m.FieldName == "Status" && m.DisplayValue == "Active");
    }

    [Fact]
    public void PopulateSelectedItemMetadata_WithoutMetadataFields_DoesNothing()
    {
        // Arrange
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var selectedItem = new ItemDto
        {
            Id = "itemId",
            Name = "File.rvt",
            Type = ItemType.File,
            ItemUrn = itemUrn
        };

        var viewModel = CreateViewModel();
        viewModel.AvailableMetadataFields = new ObservableCollection<MetadataFieldDisplayDto>();
        viewModel.SelectedItem = selectedItem;

        // Act
        var populateMethod = typeof(FilesViewModel)
            .GetMethod("PopulateSelectedItemMetadata", BindingFlags.NonPublic | BindingFlags.Instance);
        populateMethod?.Invoke(viewModel, new object?[] { selectedItem });

        // Assert
        viewModel.SelectedItemMetadata.Should().BeEmpty();
    }

    #endregion

    #region File Selection Tests

    [Fact]
    public void OnSelectedItemChanged_WithFolderSelection_Deselects()
    {
        // Arrange
        var folder = new ItemDto
        {
            Id = "folderId",
            Name = "Subfolder",
            Type = ItemType.Folder
        };

        var file = new ItemDto
        {
            Id = "fileId",
            Name = "File.txt",
            Type = ItemType.File
        };

        var viewModel = CreateViewModel();
        viewModel.SelectedItem = file;

        // Act - Try to select a folder
        viewModel.SelectedItem = folder;

        // Assert - Folder selection should be prevented, file should still be selected
        viewModel.SelectedItem.Should().Be(file);
    }

    [Fact]
    public void OnSelectedItemChanged_WithFileSelection_Selects()
    {
        // Arrange
        var file1 = new ItemDto
        {
            Id = "fileId1",
            Name = "File1.txt",
            Type = ItemType.File
        };

        var file2 = new ItemDto
        {
            Id = "fileId2",
            Name = "File2.txt",
            Type = ItemType.File
        };

        var viewModel = CreateViewModel();
        viewModel.SelectedItem = file1;

        // Act - Select another file
        viewModel.SelectedItem = file2;

        // Assert - New file should be selected
        viewModel.SelectedItem.Should().Be(file2);
    }

    #endregion

    #region Resource cleanup

    [Fact]
    public void Dispose_StopsTheDispatcherTimers()
    {
        var viewModel = CreateViewModel();

        StartAutoRefreshTimer(viewModel);
        AutoRefreshTimer(viewModel).IsEnabled.Should().BeTrue();

        viewModel.Dispose();

        // A DispatcherTimer holds its Tick handler, and so this ViewModel, and the dispatcher holds
        // the timer - so a running timer is never collectable and keeps firing for as long as the
        // dispatcher lives. Nothing disposed this ViewModel before: WPF does not dispose a window's
        // DataContext and the container holds it for the application's lifetime.
        AutoRefreshTimer(viewModel).IsEnabled.Should().BeFalse();
        ClipboardStatusTimer(viewModel).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var viewModel = CreateViewModel();

        viewModel.Dispose();

        // Called from App.OnExit, which may also run after an explicit sign-out - a second call must
        // not throw on already-disposed cancellation sources.
        var act = viewModel.Dispose;
        act.Should().NotThrow();
    }

    [Fact]
    public void ResetForSignOut_StopsTheAutoRefreshTimerAndClearsProjectState()
    {
        var viewModel = CreateViewModel();

        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";
        viewModel.Items = new ObservableCollection<ItemDto>
        {
            new() { Id = "f1", Name = "File1.rvt", Type = ItemType.File }
        };
        viewModel.AvailableMetadataFields.Add(
            new MetadataFieldDisplayDto("Status", "string", null, true, [], null, 301));

        StartAutoRefreshTimer(viewModel);

        viewModel.ResetForSignOut();

        // Logout used to clear Items only, which left the five-minute auto-refresh running against a
        // project the application no longer had a token for - producing a failed load and an error
        // banner minutes after the user signed out.
        AutoRefreshTimer(viewModel).IsEnabled.Should().BeFalse();
        viewModel.Items.Should().BeEmpty();
        viewModel.AvailableMetadataFields.Should().BeEmpty();
        viewModel.CurrentProjectId.Should().BeNull();
        viewModel.CurrentHubId.Should().BeNull();
    }

    #endregion

    #region Cancelling a load

    [Fact]
    public void CancelLoadCommand_IsOnlyEnabledWhileLoading()
    {
        var viewModel = CreateViewModel();

        viewModel.CancelLoadCommand.CanExecute(null).Should().BeFalse();

        SetIsLoading(viewModel, true);
        viewModel.CancelLoadCommand.CanExecute(null).Should().BeTrue();

        SetIsLoading(viewModel, false);
        viewModel.CancelLoadCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CancelLoad_WhileLoading_MarksCancellingImmediately()
    {
        var viewModel = CreateViewModel();

        SetIsLoading(viewModel, true);
        viewModel.CancelLoadCommand.Execute(null);

        // Set on click, not when the load actually stops: cancellation is only observed after each
        // awaited APS call, so a request already in flight finishes first and the button would
        // otherwise look like it had done nothing.
        viewModel.IsCancellingLoad.Should().BeTrue();
        viewModel.LoadingStatusText.Should().Be("Cancelling...");
    }

    [Fact]
    public void CancelLoad_WhenNotLoading_DoesNothing()
    {
        var viewModel = CreateViewModel();

        viewModel.CancelLoadCommand.Execute(null);

        viewModel.IsCancellingLoad.Should().BeFalse();
        viewModel.LoadingStatusText.Should().BeNull();
    }

    [Fact]
    public void IsLoading_WhenANewLoadStarts_ClearsLeftoverCancellingState()
    {
        var viewModel = CreateViewModel();

        SetIsLoading(viewModel, true);
        viewModel.CancelLoadCommand.Execute(null);
        SetIsLoading(viewModel, false);

        SetIsLoading(viewModel, true);

        // Otherwise the next load would open already showing "Cancelling..." with a disabled button.
        viewModel.IsCancellingLoad.Should().BeFalse();
    }

    #endregion

    #region Adding newly created files without a reload

    [Fact]
    public async Task AddCreatedItemsAsync_AddsTheRowWithoutReloadingTheProject()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";
        viewModel.Items = new ObservableCollection<ItemDto>
        {
            new() { Id = "existing", Name = "Existing.rvt", Type = ItemType.File, ItemUrn = "urn:existing" }
        };

        _dataManagementService
            .GetItemPathMetadataAsync("b.project1", "urn:new", Arg.Any<CancellationToken>())
            .Returns(new ItemPathMetadataDto
            {
                ItemUrn = "urn:new",
                PathInProject = "Project Files/WIP/AR Architect",
                WebViewLink = "https://acc.test/new"
            });

        var created = new ItemDto
        {
            Id = "new",
            Name = "P01-AR-0001.docx",
            Type = ItemType.File,
            ItemUrn = "urn:new",
            ParentId = "disc1",
            Location = "Project Files/WIP/AR Architect",
            LifecycleStage = CdeLifecycleStage.Wip
        };

        await viewModel.AddCreatedItemsAsync([created]);

        // The whole point: the new row appears, and the project is NOT re-walked. A full reload used
        // to re-list every folder, re-request a path for every pre-existing file and re-fetch the
        // attribute batch for all of them, to surface one file the creation response had already
        // described.
        viewModel.Items.Should().HaveCount(2);
        await _dataManagementService.DidNotReceive().GetFolderItemsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _dataManagementService.DidNotReceive().GetItemCustomAttributesAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());

        // Only the new file's own path is requested - one request, not one per file in the project.
        await _dataManagementService.Received(1).GetItemPathMetadataAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var added = viewModel.Items.Single(i => i.Id == "new");
        added.WebViewLink.Should().Be("https://acc.test/new");
        added.PathInProject.Should().Be("Project Files/WIP/AR Architect");
        added.Location.Should().Be("Project Files/WIP/AR Architect");
        added.LifecycleStage.Should().Be(CdeLifecycleStage.Wip);
    }

    [Fact]
    public async Task AddCreatedItemsAsync_CarriesTheAttributesTheCallerJustWrote()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = "b.project1";

        var created = new ItemDto
        {
            Id = "new", Name = "P01-AR-0001.docx", Type = ItemType.File, ItemUrn = "urn:new"
        };

        var attributes = new Dictionary<string, IReadOnlyDictionary<string, string?>>
        {
            ["urn:new"] = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "S0",
                ["Revision"] = "P01.01"
            }
        };

        await viewModel.AddCreatedItemsAsync([created], attributes);

        // Supplied by the caller because they are the values it just sent to ACC - reading them back
        // would be another request per file to learn what the application itself chose.
        viewModel.Items.Single().CustomAttributes.Should().ContainKey("Status")
            .WhoseValue.Should().Be("S0");
    }

    [Fact]
    public async Task AddCreatedItemsAsync_WhenTheFileIsAlreadyListed_DoesNotDuplicateIt()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = "b.project1";
        viewModel.Items = new ObservableCollection<ItemDto>
        {
            new() { Id = "new", Name = "P01-AR-0001.docx", Type = ItemType.File, ItemUrn = "urn:new" }
        };

        var created = new ItemDto
        {
            Id = "new", Name = "P01-AR-0001.docx", Type = ItemType.File, ItemUrn = "urn:new"
        };

        await viewModel.AddCreatedItemsAsync([created]);

        // An auto-refresh tick or a manual Refresh can pick the file up between creation and this
        // call.
        viewModel.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddCreatedItemsAsync_WhenThePathLookupFails_StillKeepsTheRow()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = "b.project1";

        _dataManagementService
            .GetItemPathMetadataAsync("b.project1", "urn:new", Arg.Any<CancellationToken>())
            .Returns<Task<ItemPathMetadataDto?>>(_ => throw new HttpRequestException("APS unavailable"));

        var created = new ItemDto
        {
            Id = "new",
            Name = "P01-AR-0001.docx",
            Type = ItemType.File,
            ItemUrn = "urn:new",
            Location = "Project Files/WIP/AR Architect"
        };

        await viewModel.AddCreatedItemsAsync([created]);

        // The row is already correct and usable without a web link, and the next refresh fills it
        // in. Failing here would mean reloading the whole project just to recover a hyperlink.
        viewModel.Items.Should().HaveCount(1);
        viewModel.Items[0].Location.Should().Be("Project Files/WIP/AR Architect");
    }

    [Fact]
    public async Task AddCreatedItemsAsync_WithNothingCreated_DoesNothing()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentProjectId = "b.project1";

        await viewModel.AddCreatedItemsAsync([]);

        viewModel.Items.Should().BeEmpty();
        await _dataManagementService.DidNotReceive().GetItemPathMetadataAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Refresh

    [Fact]
    public async Task RefreshCommand_DropsTheProjectCacheFirst()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";

        _dataManagementService
            .GetRootFolderAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(new FolderDto { Id = "root", Name = "Project Files" });

        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Caching the project's schema and its files' paths is what removed the repeated APS
        // traffic; without this an explicit Refresh would re-read the cache and look broken when a
        // change made in ACC's web UI failed to appear.
        _dataManagementService.Received(1).InvalidateProjectCache("b.project1");
        _cdeStructureUseCase.Received(1).Invalidate("b.project1");
    }

    [Fact]
    public async Task LoadFolderItemsAsync_UsesTheTokenFromTheTokenProvider()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";
        _dataManagementService
            .GetRootFolderAsync("hub1", "b.project1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FolderDto { Id = "root", Name = "Project Files" });

        await viewModel.LoadFolderItemsAsync();

        // The ViewModel holds no token of its own; the one passed down is whatever the provider
        // returned for this call.
        await _dataManagementService.Received(1).GetRootFolderAsync(
            "hub1", "b.project1", "token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AutoRefreshTick_WhilePaused_IssuesNoApsRequests()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";

        viewModel.SetAutoRefreshPaused(true);
        InvokeAutoRefreshTick(viewModel);

        _dataManagementService.DidNotReceive().GetRootFolderAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetAutoRefreshPaused_ResumingAfterASkippedTick_RefreshesOnce()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";
        _dataManagementService
            .GetRootFolderAsync("hub1", "b.project1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FolderDto { Id = "root", Name = "Project Files" });

        viewModel.SetAutoRefreshPaused(true);
        InvokeAutoRefreshTick(viewModel);
        InvokeAutoRefreshTick(viewModel);
        viewModel.SetAutoRefreshPaused(false);

        // Two skipped ticks still mean one catch-up load, not two.
        _dataManagementService.Received(1).GetRootFolderAsync(
            "hub1", "b.project1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SetAutoRefreshPaused_ResumingWithNoSkippedTick_DoesNotRefresh()
    {
        var viewModel = CreateViewModel();
        viewModel.CurrentHubId = "hub1";
        viewModel.CurrentProjectId = "b.project1";

        viewModel.SetAutoRefreshPaused(true);
        viewModel.SetAutoRefreshPaused(false);

        _dataManagementService.DidNotReceive().GetRootFolderAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test helpers

    private static DigitalMIDP.Application.Interfaces.ITokenProvider TokenProvider()
    {
        var provider = Substitute.For<DigitalMIDP.Application.Interfaces.ITokenProvider>();
        provider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
        return provider;
    }

    private static System.Windows.Threading.DispatcherTimer AutoRefreshTimer(FilesViewModel viewModel) =>
        Field<System.Windows.Threading.DispatcherTimer>(viewModel, "_autoRefreshTimer");

    private static System.Windows.Threading.DispatcherTimer ClipboardStatusTimer(FilesViewModel viewModel) =>
        Field<System.Windows.Threading.DispatcherTimer>(viewModel, "_clipboardStatusTimer");

    /// <summary>
    /// Starts the auto-refresh timer the way a completed load does. Reached by reflection because
    /// the timers are private implementation details - what is being asserted is that disposal and
    /// sign-out stop them, which is not otherwise observable.
    /// </summary>
    private static void StartAutoRefreshTimer(FilesViewModel viewModel) =>
        AutoRefreshTimer(viewModel).Start();

    /// <summary>Raises one auto-refresh tick without waiting out the five-minute interval.</summary>
    private static void InvokeAutoRefreshTick(FilesViewModel viewModel) =>
        typeof(FilesViewModel)
            .GetMethod("AutoRefreshTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(viewModel, [null, EventArgs.Empty]);

    /// <summary>
    /// Sets IsLoading, whose setter is private (the property is driven by LoadFolderItemsAsync).
    /// Going through the generated property rather than the backing field is what fires
    /// OnIsLoadingChanged, which is the logic under test for the cancel command's enabled state.
    /// </summary>
    private static void SetIsLoading(FilesViewModel viewModel, bool value) =>
        typeof(FilesViewModel)
            .GetProperty("IsLoading")!
            .SetValue(viewModel, value);

    private static T Field<T>(FilesViewModel viewModel, string name) =>
        (T)typeof(FilesViewModel)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(viewModel)!;

    #endregion
}
