using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Services;
using DigitalMIDP.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

/// <summary>
/// Unit tests for FileFilterViewModel, focusing on filter option display with descriptions.
/// </summary>
public sealed class FileFilterViewModelTests
{
    private readonly FileFilterViewModel _filterViewModel;
    private readonly ILogger<FileFilterViewModel> _logger;

    public FileFilterViewModelTests()
    {
        _logger = Substitute.For<ILogger<FileFilterViewModel>>();
        _filterViewModel = new FileFilterViewModel(
            new FilterPreferencesStore(Substitute.For<ILogger<FilterPreferencesStore>>()),
            _logger);
    }

    [Fact]
    public void FilterOptionViewModel_WithDescription_DisplaysDescription()
    {
        // Arrange
        var option = new FilterOptionViewModel("001", "In Progress", isChecked: true);

        // Act & Assert
        option.Value.Should().Be("001");
        option.Description.Should().Be("In Progress");
        option.DisplayText.Should().Be("In Progress");
        option.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void FilterOptionViewModel_WithoutDescription_DisplaysValue()
    {
        // Arrange
        var option = new FilterOptionViewModel("001", null, isChecked: true);

        // Act & Assert
        option.Value.Should().Be("001");
        option.Description.Should().BeNull();
        option.DisplayText.Should().Be("001");
    }

    [Fact]
    public void FilterOptionViewModel_WithNullValue_DisplaysBlank()
    {
        // Arrange
        var option = new FilterOptionViewModel(null, null, isChecked: true);

        // Act & Assert
        option.Value.Should().BeNull();
        option.Description.Should().BeNull();
        option.DisplayText.Should().Be("(Blank)");
    }

    [Fact]
    public void FilterOptionViewModel_WithNullValueButDescription_DisplaysDescription()
    {
        // Arrange - This is edge case: null value with description shouldn't normally happen
        var option = new FilterOptionViewModel(null, "Missing", isChecked: true);

        // Act & Assert
        option.DisplayText.Should().Be("Missing");
    }

    [Fact]
    public void FilterOptionViewModel_BackwardCompatibleConstructor_NoDescription()
    {
        // Arrange
        var option = new FilterOptionViewModel("002", isChecked: false);

        // Act & Assert
        option.Value.Should().Be("002");
        option.Description.Should().BeNull();
        option.DisplayText.Should().Be("002");
        option.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void AttributeFilterViewModel_RebuildOptions_WithDescriptions_CreatesOptionsWithDescriptions()
    {
        // Arrange
        var items = new List<ItemDto>
        {
            new() { Id = "item1", Name = "Test 1", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Status"] = "001" }.AsReadOnly() },
            new() { Id = "item2", Name = "Test 2", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Status"] = "002" }.AsReadOnly() }
        };

        var optionDescriptions = new Dictionary<string?, string?>
        {
            ["001"] = "In Progress",
            ["002"] = "Complete"
        };

        var filterViewModel = new AttributeFilterViewModel("Status", "Status", i => i.CustomAttributes?.GetValueOrDefault("Status"));

        // Act
        filterViewModel.RebuildOptions(items, new HashSet<string?>(), optionDescriptions);

        // Assert
        filterViewModel.Options.Should().HaveCount(2);

        var option001 = filterViewModel.Options.FirstOrDefault(o => o.Value == "001");
        option001.Should().NotBeNull();
        option001!.Description.Should().Be("In Progress");
        option001.DisplayText.Should().Be("In Progress");
        option001.IsChecked.Should().BeTrue();

        var option002 = filterViewModel.Options.FirstOrDefault(o => o.Value == "002");
        option002.Should().NotBeNull();
        option002!.Description.Should().Be("Complete");
        option002.DisplayText.Should().Be("Complete");
        option002.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void AttributeFilterViewModel_RebuildOptions_WithoutDescriptions_UsesValues()
    {
        // Arrange
        var items = new List<ItemDto>
        {
            new() { Id = "item1", Name = "Test 1", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Discipline"] = "ARCH" }.AsReadOnly() },
            new() { Id = "item2", Name = "Test 2", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Discipline"] = "MEP" }.AsReadOnly() }
        };

        var filterViewModel = new AttributeFilterViewModel("Discipline", "Discipline", i => i.CustomAttributes?.GetValueOrDefault("Discipline"));

        // Act - no descriptions provided
        filterViewModel.RebuildOptions(items, new HashSet<string?>(), null);

        // Assert
        filterViewModel.Options.Should().HaveCount(2);

        var archOption = filterViewModel.Options.FirstOrDefault(o => o.Value == "ARCH");
        archOption.Should().NotBeNull();
        archOption!.Description.Should().BeNull();
        archOption.DisplayText.Should().Be("ARCH");

        var mepOption = filterViewModel.Options.FirstOrDefault(o => o.Value == "MEP");
        mepOption.Should().NotBeNull();
        mepOption!.Description.Should().BeNull();
        mepOption.DisplayText.Should().Be("MEP");
    }

    [Fact]
    public void AttributeFilterViewModel_Matches_UsesRawValue_IgnoresDisplayText()
    {
        // Arrange
        var items = new List<ItemDto>
        {
            new() { Id = "item1", Name = "Test 1", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Status"] = "001" }.AsReadOnly() }
        };

        var optionDescriptions = new Dictionary<string?, string?>
        {
            ["001"] = "In Progress"
        };

        var filterViewModel = new AttributeFilterViewModel("Status", "Status", i => i.CustomAttributes?.GetValueOrDefault("Status"));
        filterViewModel.RebuildOptions(items, new HashSet<string?>(), optionDescriptions);

        // Uncheck the "In Progress" option
        filterViewModel.Options[0].IsChecked = false;

        // Act & Assert
        var testItem = items[0];
        filterViewModel.Matches(testItem).Should().BeFalse("because Status=001 is unchecked");
    }

    [Fact]
    public void FileFilterViewModel_Rebuild_WithNullValueOptions_FiltersThemOut()
    {
        // Arrange - Simulate MetadataFieldDisplayDto.Options with some null/empty values
        // (edge case from APS where options might be incomplete)
        var metadataFields = new List<MetadataFieldDisplayDto>
        {
            new MetadataFieldDisplayDto(
                Name: "Status",
                Type: "string",
                Description: "Status field",
                Optional: false,
                Options: new List<MetadataOptionDisplayDto>
                {
                    new("001", "In Progress"),
                    new(null, "NullValue"),        // Null value - should be filtered
                    new("", "EmptyValue"),         // Empty value - should be filtered
                    new("002", "Complete")
                }.AsReadOnly(),
                DefaultValue: null,
                AttributeId: 1)
        };

        var items = new List<ItemDto>
        {
            new() { Id = "item1", Name = "Test 1", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Status"] = "001" }.AsReadOnly() }
        };

        // Act - Build filters (should not throw even with null/empty values)
        _filterViewModel.SetProject("test-project");
        _filterViewModel.Rebuild(items, metadataFields);

        // Assert - Filter should have been built without errors
        var statusFilter = _filterViewModel.ColumnFilters.FirstOrDefault(f => f.ColumnKey == "Status");
        statusFilter.Should().NotBeNull("Status filter should be created");

        // Only valid options with non-null/non-empty values should be in the filter
        statusFilter!.Options.Count.Should().Be(2, "Should only have 2 options (null and empty filtered out)");
        statusFilter.Options.Should().Satisfy(
            opt => opt.Value == "001" && opt.Description == "In Progress",
            opt => opt.Value == "002" && opt.Description == "Complete"
        );
    }

    [Fact]
    public void FileFilterViewModel_Rebuild_WithFieldHavingNoOptions_DoesNotThrow()
    {
        // Arrange - Metadata field without options (e.g. text/date field)
        var metadataFields = new List<MetadataFieldDisplayDto>
        {
            new MetadataFieldDisplayDto(
                Name: "Comments",
                Type: "string",
                Description: "Comments field",
                Optional: true,
                Options: new List<MetadataOptionDisplayDto>().AsReadOnly(),
                DefaultValue: null,
                AttributeId: 2)
        };

        var items = new List<ItemDto>
        {
            new() { Id = "item1", Name = "Test 1", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?> { ["Comments"] = "Note 1" }.AsReadOnly() }
        };

        // Act & Assert - Should not throw NullReferenceException
        _filterViewModel.SetProject("test-project");
        var act = () => _filterViewModel.Rebuild(items, metadataFields);
        act.Should().NotThrow();

        var commentsFilter = _filterViewModel.ColumnFilters.FirstOrDefault(f => f.ColumnKey == "Comments");
        commentsFilter.Should().NotBeNull();
        commentsFilter!.Options.Count.Should().Be(1);
        commentsFilter.Options[0].Value.Should().Be("Note 1");
    }

    [Fact]
    public void Rebuild_WithBothTitleNames_KeepsIndependentFiltersAndSearchesDeliverableTitle()
    {
        var items = new List<ItemDto>
        {
            new() { Id = "1", Name = "First.rvt", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?>
                { [ColumnKeys.DeliverableTitle] = "Deliverable A", ["Title"] = "Generic A" }.AsReadOnly() },
            new() { Id = "2", Name = "Second.rvt", Type = ItemType.File, CustomAttributes = new Dictionary<string, string?>
                { [ColumnKeys.DeliverableTitle] = "Deliverable B", ["Title"] = "Generic B" }.AsReadOnly() }
        };
        var fields = new List<MetadataFieldDisplayDto>
        {
            new("Title", "string", null, true, [], null, AttributeId: 1),
            new(ColumnKeys.DeliverableTitle, "string", null, true, [], null, AttributeId: 2)
        };

        _filterViewModel.Rebuild(items, fields);

        _filterViewModel.ColumnFilters.Count(f => f.ColumnKey == ColumnKeys.DeliverableTitle).Should().Be(1);
        var staticFilter = _filterViewModel.TitleFilter;
        staticFilter.Should().NotBeNull();
        staticFilter!.DisplayName.Should().Be(ColumnKeys.DeliverableTitle);
        staticFilter.Options.Select(o => o.Value).Should().BeEquivalentTo("Deliverable A", "Deliverable B");
        var genericFilter = _filterViewModel.ColumnFilters.Single(f => f.ColumnKey == "Title");
        genericFilter.Options.Select(o => o.Value).Should().BeEquivalentTo("Generic A", "Generic B");

        staticFilter.Options.Single(o => o.Value == "Deliverable A").IsChecked = false;
        _filterViewModel.Matches(items[0]).Should().BeFalse();
        _filterViewModel.Matches(items[1]).Should().BeTrue();

        staticFilter.Options.Single(o => o.Value == "Deliverable A").IsChecked = true;
        _filterViewModel.SearchText = "Generic A";
        _filterViewModel.Matches(items[0]).Should().BeFalse();
        _filterViewModel.SearchText = "Deliverable A";
        _filterViewModel.Matches(items[0]).Should().BeTrue();
    }

    [Fact]
    public void Rebuild_WithoutDeliverableTitle_LeavesStaticFilterBlankInsteadOfUsingGenericTitle()
    {
        var item = new ItemDto { Id = "1", Name = "First.rvt", Type = ItemType.File,
            CustomAttributes = new Dictionary<string, string?> { ["Title"] = "Generic A" }.AsReadOnly() };

        _filterViewModel.Rebuild([item], [new("Title", "string", null, true, [], null, AttributeId: 1)]);

        _filterViewModel.TitleFilter!.Options.Should().ContainSingle().Which.Value.Should().BeNull();
        _filterViewModel.ColumnFilters.Single(f => f.ColumnKey == "Title")
            .Options.Should().ContainSingle().Which.Value.Should().Be("Generic A");
    }
}
