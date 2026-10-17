using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

/// <summary>
/// Unit tests for CreateDeliverableUseCase, the orchestrator for the "create a deliverable from a
/// template" feature: allocating a document number, resolving/creating the WIP discipline folder,
/// creating the file, and writing its attributes.
/// </summary>
public sealed class CreateDeliverableUseCaseTests
{
    private readonly ICdeStructureUseCase _cdeStructureUseCase;
    private readonly IDocumentNumberUseCase _documentNumberUseCase;
    private readonly IDataManagementService _dataManagementService;
    private readonly CreateDeliverableUseCase _useCase;

    public CreateDeliverableUseCaseTests()
    {
        _cdeStructureUseCase = Substitute.For<ICdeStructureUseCase>();
        _documentNumberUseCase = Substitute.For<IDocumentNumberUseCase>();
        _dataManagementService = Substitute.For<IDataManagementService>();
        _useCase = new CreateDeliverableUseCase(
            _cdeStructureUseCase, _documentNumberUseCase, _dataManagementService,
            Substitute.For<ILogger<CreateDeliverableUseCase>>());
    }

    private static NamingConventionFieldDto Field(int id, string name) =>
        new(id, name, "string", false, 0, new List<MetadataOptionDisplayDto>(), null);

    private static NamingConventionDto ConfiguredConvention(IReadOnlyList<MetadataFieldDisplayDto>? metadata = null) => new(
        "std1", "Standard", "-",
        Fields: new List<NamingConventionFieldDto> { Field(101, "Project"), Field(102, "Role"), Field(103, "Number") },
        Metadata: metadata ?? new List<MetadataFieldDisplayDto>());

    private static FolderDto MakeFolder(string id, string name) => new() { Id = id, Name = name };

    private static DisciplineFolderDto MakeDiscipline(FolderDto? folder) =>
        new("AR", "Architect", "AR Architect", folder);

    private static CdeStructureDto MakeStructure(FolderDto? wip, FolderDto? templates, DisciplineFolderDto discipline) => new(
        LifecycleFolders: wip is null
            ? new Dictionary<CdeLifecycleStage, FolderDto>()
            : new Dictionary<CdeLifecycleStage, FolderDto> { { CdeLifecycleStage.Wip, wip } },
        TemplatesFolder: templates,
        Disciplines: new List<DisciplineFolderDto> { discipline });

    private static ItemDto CreatedItem(string? tipVersionUrn = "urn:version:1") => new()
    {
        Id = "created-item",
        Name = "P01-AR-0001.docx",
        Type = ItemType.File,
        TipVersionUrn = tipVersionUrn
    };

    private static DocumentNumberDto MakeNumber(string sequence = "0001") => new(
        new List<DocumentNumberSegmentDto>
        {
            new(101, "Project", "P01"),
            new(102, "Role", "AR"),
            new(103, "Number", sequence)
        }, "-");

    private static readonly DeliverableTemplateDto DefaultTemplate =
        new("template-item", "Report Template.docx", "urn:template", ".docx", 100, null);

    /// <summary>
    /// Builds a request with a template by default. Pass <paramref name="useTemplate"/> false for
    /// the explicit "no template" path - a plain null default parameter can't distinguish "use the
    /// default template" from "the caller wants no template at all".
    /// </summary>
    private CreateDeliverableRequestDto MakeRequest(
        bool useTemplate = true,
        IReadOnlyDictionary<int, string?>? metadataValues = null) => new(
        HubId: "hub1",
        ProjectId: "b.project1",
        AccessToken: "token",
        Template: useTemplate ? DefaultTemplate : null,
        DisciplineCode: "AR",
        FieldValues: new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } },
        MetadataValues: metadataValues ?? new Dictionary<int, string?>());

    private void SetUpHappyPathStructure(FolderDto? wipDisciplineFolder)
    {
        var wip = MakeFolder("wip1", "WIP");
        var templates = MakeFolder("templates1", "Templates");
        var discipline = MakeDiscipline(wipDisciplineFolder);
        var structure = MakeStructure(wip, templates, discipline);

        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention());
        _cdeStructureUseCase.GetStructureAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(structure);
    }

    #region CreateAsync - happy path

    [Fact]
    public async Task CreateAsync_WithTemplate_CreatesFileAndAppliesAttributes()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                "b.project1", Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                "token", Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));

        var createdItem = CreatedItem();
        _dataManagementService.CreateItemFromTemplateAsync(
                "b.project1", "disc1", "template-item", "P01-AR-0001.docx", Arg.Any<CancellationToken>())
            .Returns(createdItem);
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeTrue();
        result.FileName.Should().Be("P01-AR-0001.docx");
        result.DocumentNumber.Should().Be("P01-AR-0001");
        result.DestinationFolder.Should().Be("AR Architect");
        result.AttributesApplied.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithoutTemplate_CreatesEmptyFile()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));

        var createdItem = CreatedItem();
        _dataManagementService.CreateItemAsync("b.project1", "disc1", "P01-AR-0001", Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(createdItem);

        var result = await _useCase.CreateAsync(MakeRequest(useTemplate: false));

        result.Success.Should().BeTrue();
        result.FileName.Should().Be("P01-AR-0001"); // no extension without a template
        await _dataManagementService.Received(1).CreateItemAsync(
            "b.project1", "disc1", "P01-AR-0001", Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        await _dataManagementService.DidNotReceive().CreateItemFromTemplateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WritesSuppliedDescriptionWithoutDerivingItFromTitleFields()
    {
        var descriptionField = new MetadataFieldDisplayDto("Description", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 201);
        var title1 = new MetadataFieldDisplayDto("Title1", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 202);
        var title2 = new MetadataFieldDisplayDto("Title2", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 203);
        var title3 = new MetadataFieldDisplayDto("Title3", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 204);

        var wip = MakeFolder("wip1", "WIP");
        var templates = MakeFolder("templates1", "Templates");
        var discipline = MakeDiscipline(MakeFolder("disc1", "AR Architect"));
        var structure = MakeStructure(wip, templates, discipline);

        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention(new List<MetadataFieldDisplayDto> { descriptionField, title1, title2, title3 }));
        _cdeStructureUseCase.GetStructureAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(structure);
        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));
        _dataManagementService.CreateItemFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreatedItem());
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        var request = MakeRequest(metadataValues: new Dictionary<int, string?>
        {
            { 201, "Direct Deliverable Title" },
            { 202, "Ground Floor" },
            { 203, "" },
            { 204, "Plan" }
        });

        var result = await _useCase.CreateAsync(request);

        result.Success.Should().BeTrue();
        await _dataManagementService.Received(1).UpdateItemCustomAttributesAsync(
            "b.project1", "urn:version:1",
            Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u =>
                u.Any(x => x.AttributeId == 201 && x.Value == "Direct Deliverable Title")
                && u.Any(x => x.AttributeId == 202 && x.Value == "Ground Floor")
                && u.Any(x => x.AttributeId == 204 && x.Value == "Plan")
                && u.All(x => x.AttributeId != 203)),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region CreateAsync - discipline folder creation

    [Fact]
    public async Task CreateAsync_WithMissingDisciplineFolder_CreatesItAndInvalidatesCache()
    {
        SetUpHappyPathStructure(wipDisciplineFolder: null);

        _dataManagementService.CreateFolderAsync("b.project1", "wip1", "AR Architect", Arg.Any<CancellationToken>())
            .Returns(MakeFolder("new-disc-folder", "AR Architect"));
        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));
        _dataManagementService.CreateItemFromTemplateAsync(
                "b.project1", "new-disc-folder", "template-item", "P01-AR-0001.docx", Arg.Any<CancellationToken>())
            .Returns(CreatedItem());
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Contains("AR Architect"));
        _cdeStructureUseCase.Received(1).Invalidate("b.project1");
        await _dataManagementService.Received(1).CreateFolderAsync("b.project1", "wip1", "AR Architect", Arg.Any<CancellationToken>());
    }

    #endregion

    #region CreateAsync - refusal paths

    [Fact]
    public async Task CreateAsync_WithNoNamingConvention_ReturnsFailedResult()
    {
        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(NamingConventionDto.None);

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("naming standard");
        await _cdeStructureUseCase.DidNotReceive().GetStructureAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithNoWipFolder_ReturnsFailedResult()
    {
        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention());
        _cdeStructureUseCase.GetStructureAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(MakeStructure(wip: null, templates: MakeFolder("t1", "Templates"), MakeDiscipline(null)));

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("WIP");
    }

    [Fact]
    public async Task CreateAsync_WithUnknownDiscipline_ReturnsFailedResult()
    {
        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention());
        _cdeStructureUseCase.GetStructureAsync("hub1", "b.project1", "token", Arg.Any<CancellationToken>())
            .Returns(MakeStructure(MakeFolder("wip1", "WIP"), MakeFolder("t1", "Templates"), MakeDiscipline(MakeFolder("d1", "ST Structural")) with { Code = "ST" }));

        var result = await _useCase.CreateAsync(MakeRequest()); // request asks for "AR", only "ST" exists

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("AR");
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateDocumentNumber_ReturnsFailedResultWithSuggestion()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), new DocumentNumberAvailabilityDto("P01-AR-0001", false, "P01-AR-0001.docx", "P01-AR-0002")));

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("P01-AR-0001.docx");
        result.SuggestedDocumentNumber.Should().Be("P01-AR-0002");
        await _dataManagementService.DidNotReceive().CreateItemFromTemplateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithPermissionDenied_ReturnsFailedResult()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DocumentNumberAllocationDto>>(_ => throw new PermissionDeniedException("No data:create scope."));

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("data:create");
    }

    [Fact]
    public async Task CreateAsync_WithApplicationException_ReturnsFailedResult()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DocumentNumberAllocationDto>>(_ => throw new global::DigitalMIDP.Application.Exceptions.ApplicationException("ACC is unreachable."));

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("ACC is unreachable.");
    }

    [Fact]
    public async Task CreateAsync_WithUnexpectedException_ReturnsFailedResultWithMessage()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DocumentNumberAllocationDto>>(_ => throw new InvalidOperationException("Boom."));

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Boom.");
    }

    [Fact]
    public async Task CreateAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var act = () => _useCase.CreateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region CreateAsync - attribute write outcomes

    [Fact]
    public async Task CreateAsync_WithMissingTipVersionUrn_ReportsWarningAndAttributesNotApplied()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));
        _dataManagementService.CreateItemFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreatedItem(tipVersionUrn: null!));

        var result = await _useCase.CreateAsync(MakeRequest());

        result.Success.Should().BeTrue(); // the file itself was created successfully
        result.AttributesApplied.Should().BeFalse();
        result.Warnings.Should().ContainSingle(w => w.Contains("Edit Attributes"));
        await _dataManagementService.DidNotReceive().UpdateItemCustomAttributesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithBatchAttributeRejection_RetriesIndividuallyAndSucceeds()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));
        _dataManagementService.CreateItemFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreatedItem());

        var statusField = new MetadataFieldDisplayDto("Status", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 301);
        var revisionField = new MetadataFieldDisplayDto("Revision", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 302);
        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention(new List<MetadataFieldDisplayDto> { statusField, revisionField }));

        // The batch call (more than one update) is rejected; the individual retry succeeds.
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u => u.Count > 1),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ =>
                throw new AttributeValidationException("Batch rejected."));
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u => u.Count == 1),
                Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        var request = MakeRequest(metadataValues: new Dictionary<int, string?> { { 301, "S0" }, { 302, "P01.01" } });

        var result = await _useCase.CreateAsync(request);

        result.Success.Should().BeTrue();
        result.AttributesApplied.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithBatchAttributeRejectionAndIndividualFailure_ReportsWhichFieldsFailed()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentNumberAllocationDto(MakeNumber(), DocumentNumberAvailabilityDto.Available("P01-AR-0001")));
        _dataManagementService.CreateItemFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreatedItem());

        var statusField = new MetadataFieldDisplayDto("Status", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 301);
        var revisionField = new MetadataFieldDisplayDto("Revision", "string", null, true, new List<MetadataOptionDisplayDto>(), null, 302);
        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention(new List<MetadataFieldDisplayDto> { statusField, revisionField }));

        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u => u.Count > 1),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ =>
                throw new AttributeValidationException("Batch rejected."));

        // Status (301) succeeds individually, Revision (302) keeps failing.
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u => u.Count == 1 && u.First().AttributeId == 301),
                Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Is<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(u => u.Count == 1 && u.First().AttributeId == 302),
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<CustomAttributeUpdateResultDto>>>(_ =>
                throw new AttributeValidationException("Invalid revision."));

        var request = MakeRequest(metadataValues: new Dictionary<int, string?> { { 301, "S0" }, { 302, "not-valid" } });

        var result = await _useCase.CreateAsync(request);

        result.Success.Should().BeTrue();
        result.AttributesApplied.Should().BeFalse();
        result.Warnings.Should().ContainSingle(w => w.Contains("Revision") && w.Contains("Edit Attributes"));
    }

    #endregion

    #region CreateManyAsync

    [Fact]
    public async Task CreateManyAsync_WithEmptyBatch_ReturnsEmptyResult()
    {
        var request = new CreateDeliverableBatchRequestDto(
            "hub1", "b.project1", "token", null, "AR",
            new Dictionary<int, string>(), new Dictionary<int, string?>(), []);

        var result = await _useCase.CreateManyAsync(request);

        result.Items.Should().BeEmpty();
        result.SuccessCount.Should().Be(0);
        result.AllSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task CreateManyAsync_CreatesOneDeliverablePerTitleSetAndReportsProgress()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));
        var description = new MetadataFieldDisplayDto("Description", "string", null, false, [], null, 201);
        _cdeStructureUseCase.GetNamingConventionAsync("hub1", "b.project1", Arg.Any<CancellationToken>())
            .Returns(ConfiguredConvention([description]));

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                      new DocumentNumberAllocationDto(MakeNumber("0001"), DocumentNumberAvailabilityDto.Available(MakeNumber("0001").Value)),
                      new DocumentNumberAllocationDto(MakeNumber("0002"), DocumentNumberAvailabilityDto.Available(MakeNumber("0002").Value)),
                      new DocumentNumberAllocationDto(MakeNumber("0003"), DocumentNumberAvailabilityDto.Available(MakeNumber("0003").Value)));
        _dataManagementService.CreateItemFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreatedItem());
        var writtenAttributes = new List<IReadOnlyList<CustomAttributeUpdateRequestDto>>();
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                writtenAttributes.Add(call.ArgAt<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(2).ToList());
                return Task.FromResult<IReadOnlyList<CustomAttributeUpdateResultDto>>([]);
            });

        var progressReports = new List<DeliverableBatchProgressDto>();
        var progress = new SynchronousProgress<DeliverableBatchProgressDto>(progressReports.Add);

        var request = new CreateDeliverableBatchRequestDto(
            "hub1", "b.project1", "token",
            new DeliverableTemplateDto("template-item", "Report Template.docx", "urn:template", ".docx", 100, null),
            "AR",
            new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } },
            new Dictionary<int, string?> { { 301, "S0" } },
            new List<IReadOnlyDictionary<int, string?>>
            {
                new Dictionary<int, string?> { { 201, "Title A" } },
                new Dictionary<int, string?> { { 201, "Title B" } },
                new Dictionary<int, string?> { { 201, "Title C" } }
            });

        var result = await _useCase.CreateManyAsync(request, progress);

        result.Items.Should().HaveCount(3);
        result.AllSucceeded.Should().BeTrue();
        result.SuccessCount.Should().Be(3);
        result.Items.Select(i => i.DocumentNumber).Should().Equal("P01-AR-0001", "P01-AR-0002", "P01-AR-0003");
        writtenAttributes.Should().HaveCount(3);
        writtenAttributes.Select(u => u.Single(x => x.AttributeId == 201).Value)
            .Should().Equal("Title A", "Title B", "Title C");
        writtenAttributes.Should().OnlyContain(u => u.Any(x => x.AttributeId == 301 && x.Value == "S0"));
        await _dataManagementService.Received(3).CreateItemFromTemplateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        progressReports.Should().HaveCount(3);
        progressReports.Select(p => p.Completed).Should().Equal(1, 2, 3);
        progressReports.Should().OnlyContain(p => p.Total == 3 && p.LastResult.Success);
    }

    [Fact]
    public async Task CreateManyAsync_StopsEarlyWhenCancelledBetweenItems()
    {
        SetUpHappyPathStructure(MakeFolder("disc1", "AR Architect"));

        using var cts = new CancellationTokenSource();

        _documentNumberUseCase.AllocateAndVerifyNextAsync(
                Arg.Any<string>(), Arg.Any<DisciplineFolderDto>(), Arg.Any<NamingConventionDto>(),
                Arg.Any<IReadOnlyDictionary<int, string>>(),
                // reservedNumbers: the numbers already issued in this batch, which the
                // allocator folds in so a scan that has not caught up yet cannot reissue one.
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                // Cancel while handling the first item; the loop checks the token before starting
                // the second, so the batch should stop after exactly one item.
                cts.Cancel();
                return new DocumentNumberAllocationDto(MakeNumber("0001"), DocumentNumberAvailabilityDto.Available("P01-AR-0001"));
            });
        _dataManagementService.CreateItemFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CreatedItem());
        _dataManagementService.UpdateItemCustomAttributesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyCollection<CustomAttributeUpdateRequestDto>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CustomAttributeUpdateResultDto>());

        var request = new CreateDeliverableBatchRequestDto(
            "hub1", "b.project1", "token",
            new DeliverableTemplateDto("template-item", "Report Template.docx", "urn:template", ".docx", 100, null),
            "AR",
            new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } },
            new Dictionary<int, string?>(),
            new List<IReadOnlyDictionary<int, string?>>
            {
                new Dictionary<int, string?> { { 201, "Title A" } },
                new Dictionary<int, string?> { { 201, "Title B" } }
            });

        var result = await _useCase.CreateManyAsync(request, cancellationToken: cts.Token);

        result.Items.Should().HaveCount(1);
    }

    #endregion

    /// <summary>
    /// A synchronous <see cref="IProgress{T}"/> for tests. The real <see cref="Progress{T}"/> posts
    /// callbacks via a captured <see cref="SynchronizationContext"/> (falling back to the thread pool
    /// when none exists, as in xUnit), so reports can still be in flight after the awaited call
    /// returns. This reports inline instead, so assertions can rely on the reports being complete.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
