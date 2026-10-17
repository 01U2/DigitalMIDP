using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

/// <summary>
/// Unit tests for EditCustomAttributesViewModel, covering the Edit Custom Attributes dialog's
/// initialization, validation, single/multi-file save behaviour, and error handling.
/// </summary>
public sealed class EditCustomAttributesViewModelTests
{
    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<EditCustomAttributesViewModel> _logger;
    private readonly EditCustomAttributesViewModel _viewModel;

    public EditCustomAttributesViewModelTests()
    {
        _dataManagementService = Substitute.For<IDataManagementService>();
        _logger = Substitute.For<ILogger<EditCustomAttributesViewModel>>();
        _viewModel = new EditCustomAttributesViewModel(_dataManagementService, _logger);
    }

    private static MetadataFieldDisplayDto TextField(int attributeId, string name, bool optional = true) =>
        new(name, "string", null, optional, new List<MetadataOptionDisplayDto>(), null, attributeId);

    private static MetadataFieldDisplayDto DropdownField(int attributeId, string name, bool optional, params string[] options) =>
        new(name, "array", null, optional,
            options.Select(o => new MetadataOptionDisplayDto(o, null)).ToList(), null, attributeId);

    private static ItemDto MakeItem(string id, string name, string? tipVersionUrn = "urn:version:1",
        IReadOnlyDictionary<string, string?>? customAttributes = null) => new()
    {
        Id = id,
        Name = name,
        Type = ItemType.File,
        ItemUrn = $"urn:item:{id}",
        TipVersionUrn = tipVersionUrn,
        CustomAttributes = customAttributes
    };

    #region Initialize Tests

    [Fact]
    public void Initialize_WithSingleFile_PrefillsFieldsFromCurrentValues()
    {
        var item = MakeItem("item1", "File.rvt", customAttributes: new Dictionary<string, string?>
        {
            { "Category", "Structural" }
        });
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };

        _viewModel.Initialize("b.project1", new[] { item }, fields);

        _viewModel.IsMultiFile.Should().BeFalse();
        _viewModel.FileName.Should().Be("File.rvt");
        _viewModel.Fields.Should().ContainSingle();
        _viewModel.Fields[0].Value.Should().Be("Structural");
    }

    [Fact]
    public void Initialize_WithMultipleFiles_StartsFieldsBlank()
    {
        var item1 = MakeItem("item1", "File1.rvt", customAttributes: new Dictionary<string, string?> { { "Category", "Structural" } });
        var item2 = MakeItem("item2", "File2.rvt", customAttributes: new Dictionary<string, string?> { { "Category", "MEP" } });
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };

        _viewModel.Initialize("b.project1", new[] { item1, item2 }, fields);

        _viewModel.IsMultiFile.Should().BeTrue();
        _viewModel.FileName.Should().Be("2 files selected");
        _viewModel.Fields[0].Value.Should().BeNull();
    }

    [Fact]
    public void Initialize_ExcludesFieldsWithZeroAttributeId()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto>
        {
            TextField(0, "NotWritable"),
            TextField(101, "Category")
        };

        _viewModel.Initialize("b.project1", new[] { item }, fields);

        _viewModel.Fields.Should().ContainSingle();
        _viewModel.Fields[0].Name.Should().Be("Category");
    }

    [Fact]
    public void Initialize_WithEmptyItems_ThrowsArgumentException()
    {
        var act = () => _viewModel.Initialize("b.project1", Array.Empty<ItemDto>(), new List<MetadataFieldDisplayDto>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Initialize_WithNullProjectId_ThrowsArgumentException()
    {
        var item = MakeItem("item1", "File.rvt");

        var act = () => _viewModel.Initialize(null!, new[] { item }, new List<MetadataFieldDisplayDto>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Initialize_ResetsPreviousMessagesAndSaveState()
    {
        var item = MakeItem("item1", "File.rvt");
        _viewModel.Initialize("b.project1", new[] { item }, new List<MetadataFieldDisplayDto>());

        _viewModel.ErrorMessage.Should().BeNull();
        _viewModel.SuccessMessage.Should().BeNull();
        _viewModel.SaveSucceeded.Should().BeFalse();
    }

    #endregion

    #region SaveAsync Validation Tests

    [Fact]
    public async Task SaveAsync_WithRequiredFieldLeftBlank_SetsErrorMessage()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category", optional: false) };
        _viewModel.Initialize("b.project1", new[] { item }, fields);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("'Category' is required.");
        _viewModel.SaveSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_WithInvalidDropdownOption_SetsErrorMessage()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto> { DropdownField(101, "Status", true, "Draft", "Approved") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "NotAnOption";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("'NotAnOption' is not a valid option for 'Status'.");
    }

    [Fact]
    public async Task SaveAsync_WithNoChanges_SetsNoChangesErrorMessage()
    {
        var item = MakeItem("item1", "File.rvt", customAttributes: new Dictionary<string, string?> { { "Category", "Structural" } });
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("No changes to save.");
        await _dataManagementService.DidNotReceive().UpdateItemCustomAttributesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_MultiFileWithUntouchedRequiredField_DoesNotValidateIt()
    {
        // In multi-file mode, an untouched required field must not block save.
        var item1 = MakeItem("item1", "File1.rvt");
        var item2 = MakeItem("item2", "File2.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category", optional: false) };
        _viewModel.Initialize("b.project1", new[] { item1, item2 }, fields);

        // Touch nothing -> no changes at all, should hit "No changes to save", not a required-field error.
        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("No changes to save.");
    }

    #endregion

    #region SaveAsync Happy Path Tests

    [Fact]
    public async Task SaveAsync_SingleFile_HappyPath_UpdatesAttributeAndRaisesEvent()
    {
        var item = MakeItem("item1", "File.rvt", customAttributes: new Dictionary<string, string?> { { "Category", "Structural" } });
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "MEP";

        _dataManagementService.UpdateItemCustomAttributesAsync(
                "b.project1", "urn:version:1", Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>? raisedPayload = null;
        _viewModel.AttributesUpdated += payload => raisedPayload = payload;

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.SuccessMessage.Should().Be("Custom attributes updated successfully.");
        _viewModel.SaveSucceeded.Should().BeTrue();
        _viewModel.ErrorMessage.Should().BeNull();

        raisedPayload.Should().NotBeNull();
        raisedPayload!["urn:item:item1"]["Category"].Should().Be("MEP");

        await _dataManagementService.Received(1).UpdateItemCustomAttributesAsync(
            "b.project1", "urn:version:1",
            Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u =>
                u.Count == 1 && u.First().AttributeId == 101 && u.First().Value == "MEP"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_MultiFile_AllSucceed_ReportsCountInSuccessMessage()
    {
        var item1 = MakeItem("item1", "File1.rvt");
        var item2 = MakeItem("item2", "File2.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item1, item2 }, fields);
        _viewModel.Fields[0].Value = "MEP";

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.SuccessMessage.Should().Be("Custom attributes updated successfully for all 2 files.");
        _viewModel.SaveSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_MultiFile_UntouchedFieldsAreNotSentForEachFile()
    {
        var item1 = MakeItem("item1", "File1.rvt");
        var item2 = MakeItem("item2", "File2.rvt");
        var fields = new List<MetadataFieldDisplayDto>
        {
            TextField(101, "Category"),
            TextField(102, "Status")
        };
        _viewModel.Initialize("b.project1", new[] { item1, item2 }, fields);
        _viewModel.Fields[0].Value = "MEP"; // touch only Category

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        await _viewModel.SaveCommand.ExecuteAsync(null);

        await _dataManagementService.Received(2).UpdateItemCustomAttributesAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u => u.Count == 1 && u.First().AttributeId == 101),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region SaveAsync Error Path Tests

    [Fact]
    public async Task SaveAsync_WithNoFileSelected_SetsErrorMessage()
    {
        // Never call Initialize -> no items and no project id.
        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("No file selected.");
    }

    [Fact]
    public async Task SaveAsync_WithMissingTipVersionUrn_ReportsPerFileFailure()
    {
        var item = MakeItem("item1", "File.rvt", tipVersionUrn: null);
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "MEP";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Contain("File version could not be determined");
        _viewModel.SaveSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_WithPermissionDenied_ReportsExceptionMessage()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "MEP";

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ => throw new PermissionDeniedException("Access denied."));

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Contain("Access denied.");
        _viewModel.SaveSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_WithAttributeValidationException_MapsErrorsToFieldNames()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "MEP";

        var validationEx = new AttributeValidationException(
            "Bad value",
            new List<AttributeValidationError> { new(101, "MEP", "must be one of the allowed values") });

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ => throw validationEx);

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("'File.rvt': 'Category': must be one of the allowed values");
    }

    [Fact]
    public async Task SaveAsync_WithAttributeValidationExceptionWithoutStructuredErrors_UsesExceptionMessage()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "MEP";

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ => throw new AttributeValidationException("Unstructured failure."));

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Be("'File.rvt': Unstructured failure.");
    }

    [Fact]
    public async Task SaveAsync_WithUnexpectedException_ReportsExceptionMessage()
    {
        var item = MakeItem("item1", "File.rvt");
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item }, fields);
        _viewModel.Fields[0].Value = "MEP";

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ => throw new InvalidOperationException("Boom."));

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Contain("Boom.");
        _viewModel.SaveSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_MultiFile_PartialFailure_ReportsBothCountsAndKeepsSaveSucceeded()
    {
        var item1 = MakeItem("item1", "File1.rvt");
        var item2 = MakeItem("item2", "File2.rvt", tipVersionUrn: null); // will fail
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item1, item2 }, fields);
        _viewModel.Fields[0].Value = "MEP";

        _dataManagementService.UpdateItemCustomAttributesAsync(
                "b.project1", "urn:version:1", Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.ErrorMessage.Should().Contain("Updated 1 of 2 files");
        _viewModel.ErrorMessage.Should().Contain("File2.rvt");
        _viewModel.SaveSucceeded.Should().BeTrue(); // at least one succeeded
    }

    [Fact]
    public async Task SaveAsync_MultiFile_AllFail_SaveSucceededIsFalse()
    {
        var item1 = MakeItem("item1", "File1.rvt", tipVersionUrn: null);
        var item2 = MakeItem("item2", "File2.rvt", tipVersionUrn: null);
        var fields = new List<MetadataFieldDisplayDto> { TextField(101, "Category") };
        _viewModel.Initialize("b.project1", new[] { item1, item2 }, fields);
        _viewModel.Fields[0].Value = "MEP";

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _viewModel.SaveSucceeded.Should().BeFalse();
        _viewModel.ErrorMessage.Should().Contain("File1.rvt");
        _viewModel.ErrorMessage.Should().Contain("File2.rvt");
    }

    #endregion
}
