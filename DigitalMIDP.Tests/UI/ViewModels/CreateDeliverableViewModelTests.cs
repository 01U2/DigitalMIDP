using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.UI.ViewModels;

public sealed class CreateDeliverableViewModelTests
{
    private readonly ICdeStructureUseCase _structure = Substitute.For<ICdeStructureUseCase>();
    private readonly IDataManagementService _dataManagement = Substitute.For<IDataManagementService>();
    private readonly ITemplateCatalogUseCase _templates = Substitute.For<ITemplateCatalogUseCase>();
    private readonly IDocumentNumberUseCase _numbers = Substitute.For<IDocumentNumberUseCase>();
    private readonly CreateDeliverableViewModel _viewModel;
    private readonly DisciplineFolderDto _discipline = new("AR", "Architect", "AR Architect", new FolderDto { Id = "disc1", Name = "AR Architect" });
    private readonly DeliverableTemplateDto _template = new("template1", "Template.docx", null, ".docx", null, null);

    public CreateDeliverableViewModelTests()
    {
        _viewModel = new CreateDeliverableViewModel(_structure, _dataManagement, TokenProvider(), _templates, _numbers,
            Substitute.For<ILogger<CreateDeliverableViewModel>>());
    }

    private async Task InitializeAsync(
        bool hasDescription = true,
        bool hasConventionDescription = false,
        string relatedTitleName = "Description")
    {
        var fields = new List<NamingConventionFieldDto>
        {
            new(102, "Role", "array", false, 0, [new MetadataOptionDisplayDto("AR", "Architect")], null),
            new(103, "Number", "string", false, 1, [], null)
        };
        var metadata = new List<MetadataFieldDisplayDto>
        {
            new("Title1", "string", null, true, [], null, 202),
            new("Title", "string", null, true, [], null, 203),
            new("Status", "string", null, true, [], null, 301)
        };
        if (hasConventionDescription)
        {
            metadata.Add(new MetadataFieldDisplayDto("Description", "string", null, true, [], null, 201));
        }

        var relatedMetadata = new List<MetadataFieldDisplayDto>
        {
            new("Status", "string", null, true, [], null, 301),
            new("Classification", "string", null, true, [], null, 501)
        };
        if (hasDescription)
        {
            relatedMetadata.Add(new MetadataFieldDisplayDto(relatedTitleName, "string", null, true, [], null, 401));
        }

        _dataManagement.GetProjectNamingStandardAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(new ProjectNamingStandardDto("", "Custom Attributes", relatedMetadata));

        _structure.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(new NamingConventionDto("std1", "Standard", "-", fields, metadata));
        _structure.GetStructureAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(new CdeStructureDto(
                new Dictionary<CdeLifecycleStage, FolderDto> { [CdeLifecycleStage.Wip] = new FolderDto { Id = "wip1", Name = "WIP" } },
                new FolderDto { Id = "templates1", Name = "Templates" }, [_discipline]));
        _templates.GetTemplatesAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(new List<DeliverableTemplateDto> { _template });
        _numbers.AllocateNextAsync(Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(),
                Arg.Any<NamingConventionDto>(), Arg.Any<IReadOnlyDictionary<int, string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberDto([new DocumentNumberSegmentDto(102, "Role", "AR"),
                new DocumentNumberSegmentDto(103, "Number", "0001")], "-"));

        await _viewModel.InitializeAsync("hub1", "b.project1");
    }

    private async Task AdvanceToDetailsAsync()
    {
        _viewModel.SelectedDiscipline = _discipline;
        await _viewModel.NextCommand.ExecuteAsync(null);
        _viewModel.SelectedTemplate = _template;
        await _viewModel.NextCommand.ExecuteAsync(null);
        _viewModel.CurrentStep.Should().Be(CreateDeliverableViewModel.StepDetails);
    }

    [Fact]
    public async Task SingleDocument_RequiresDeliverableTitleAndSendsDescription()
    {
        await InitializeAsync();
        await AdvanceToDetailsAsync();

        _viewModel.SharedMetadataFields.Select(f => f.Name)
            .Should().Contain("Title1").And.Contain("Title").And.Contain("Classification").And.NotContain("Description");
        _viewModel.SharedMetadataFields.Count(f => f.Name == "Status").Should().Be(1);
        await _viewModel.NextCommand.ExecuteAsync(null);
        _viewModel.ErrorMessage.Should().Contain("Deliverable Title is required");
        _viewModel.DeliverableItems[0].DeliverableTitleField.Value = "  First document  ";
        await _viewModel.NextCommand.ExecuteAsync(null);
        _viewModel.CurrentStep.Should().Be(CreateDeliverableViewModel.StepReview);

        CreateDeliverableBatchRequestDto? request = null;
        _viewModel.CreationRequested += result => request = result;
        _viewModel.CreateCommand.Execute(null);

        request.Should().NotBeNull();
        request!.PerItemTitleValues.Should().ContainSingle();
        request.PerItemTitleValues[0][401].Should().Be("First document");
        request.SharedMetadataValues.Should().NotContainKey(401);
    }

    [Fact]
    public async Task RelatedDeliverableTitleName_IsAcceptedWithoutDescriptionInNamingStandard()
    {
        await InitializeAsync(relatedTitleName: "Deliverable Title");
        await AdvanceToDetailsAsync();
        _viewModel.SharedMetadataFields.Select(f => f.Name)
            .Should().Contain("Title").And.NotContain("Deliverable Title");
        _viewModel.DeliverableItems[0].DeliverableTitleField.Value = "First drawing";
        await _viewModel.NextCommand.ExecuteAsync(null);

        CreateDeliverableBatchRequestDto? request = null;
        _viewModel.CreationRequested += result => request = result;
        _viewModel.CreateCommand.Execute(null);

        _viewModel.IsConfigured.Should().BeTrue();
        request.Should().NotBeNull();
        request!.PerItemTitleValues[0][401].Should().Be("First drawing");
    }

    [Fact]
    public async Task MultipleDocuments_RequireSeparateTitlesAndRetainEntriesWhenQuantityChanges()
    {
        await InitializeAsync(relatedTitleName: "Deliverable Title");
        _viewModel.QuantityText = "2";
        await AdvanceToDetailsAsync();
        _viewModel.DeliverableItems[0].DeliverableTitleField.Value = "First";

        await _viewModel.NextCommand.ExecuteAsync(null);
        _viewModel.ErrorMessage.Should().Contain("Document 2: Deliverable Title is required");
        _viewModel.QuantityText = "3";
        _viewModel.DeliverableItems[0].DeliverableTitleField.Value.Should().Be("First");
        _viewModel.DeliverableItems[1].DeliverableTitleField.Value = "Second";
        _viewModel.DeliverableItems[2].DeliverableTitleField.Value = "Third";
        await _viewModel.NextCommand.ExecuteAsync(null);

        CreateDeliverableBatchRequestDto? request = null;
        _viewModel.CreationRequested += result => request = result;
        _viewModel.CreateCommand.Execute(null);
        request.Should().NotBeNull();
        request!.PerItemTitleValues.Select(values => values[401]).Should().Equal("First", "Second", "Third");
    }

    [Fact]
    public async Task ConventionDescription_DoesNotOverrideRelatedDescriptionAttributeId()
    {
        await InitializeAsync(hasConventionDescription: true);
        await AdvanceToDetailsAsync();
        _viewModel.DeliverableItems[0].DeliverableTitleField.Value = "Related title";
        await _viewModel.NextCommand.ExecuteAsync(null);

        CreateDeliverableBatchRequestDto? request = null;
        _viewModel.CreationRequested += result => request = result;
        _viewModel.CreateCommand.Execute(null);

        request.Should().NotBeNull();
        request!.PerItemTitleValues[0].Should().ContainKey(401).WhoseValue.Should().Be("Related title");
        request.PerItemTitleValues[0].Should().NotContainKey(201);
        request.SharedMetadataValues.Should().NotContainKey(201);
    }

    [Fact]
    public async Task MissingDescriptionAttribute_PreventsCreation()
    {
        await InitializeAsync(hasDescription: false, hasConventionDescription: true);

        _viewModel.IsConfigured.Should().BeFalse();
        _viewModel.ErrorMessage.Should().Contain("related custom attributes");
    }

    private static DigitalMIDP.Application.Interfaces.ITokenProvider TokenProvider()
    {
        var provider = Substitute.For<DigitalMIDP.Application.Interfaces.ITokenProvider>();
        provider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
        return provider;
    }
}
