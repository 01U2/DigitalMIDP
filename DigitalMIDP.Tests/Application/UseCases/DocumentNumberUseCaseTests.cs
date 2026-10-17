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
/// Unit tests for DocumentNumberUseCase: composing, allocating and checking availability of
/// document numbers for the file-creation-from-template feature.
/// </summary>
public sealed class DocumentNumberUseCaseTests
{
    private readonly IDataManagementService _dataManagementService;
    private readonly DocumentNumberUseCase _useCase;

    public DocumentNumberUseCaseTests()
    {
        _dataManagementService = Substitute.For<IDataManagementService>();
        _useCase = new DocumentNumberUseCase(_dataManagementService, Substitute.For<ILogger<DocumentNumberUseCase>>());
    }

    private static NamingConventionFieldDto Field(int id, string name, bool optional = false, int position = 0) =>
        new(id, name, "string", optional, position, new List<MetadataOptionDisplayDto>(), null);

    private static NamingConventionDto MakeConvention() => new(
        "std1", "Standard", "-",
        Fields: new List<NamingConventionFieldDto>
        {
            Field(101, "Project", optional: false, position: 0),
            Field(102, "Role", optional: false, position: 1),
            Field(103, "Number", optional: false, position: 2) // resolved as sequential by name
        },
        Metadata: new List<MetadataFieldDisplayDto>());

    private static DisciplineFolderDto MakeDiscipline(FolderDto? folder = null) =>
        new("AR", "Architect", "AR Architect", folder);

    private static ItemDto FileItem(string urn, string name, IReadOnlyDictionary<string, string?>? attrs = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Type = ItemType.File,
        ItemUrn = urn
    };

    #region Compose

    [Fact]
    public void Compose_WithAllValuesSupplied_BuildsSegmentsInOrder()
    {
        var convention = MakeConvention();
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var number = _useCase.Compose(convention, chosenValues, "0001");

        number.Value.Should().Be("P01-AR-0001");
        number.Segments.Should().HaveCount(3);
        number.Sequence.Should().Be("0001");
        number.Prefix.Should().Be("P01-AR");
    }

    [Fact]
    public void Compose_WithNotConfiguredConvention_ThrowsApplicationException()
    {
        var act = () => _useCase.Compose(NamingConventionDto.None, new Dictionary<int, string>(), "0001");

        act.Should().Throw<global::DigitalMIDP.Application.Exceptions.ApplicationException>();
    }

    [Fact]
    public void Compose_WithMissingRequiredField_ThrowsAttributeValidationExceptionNamingIt()
    {
        var convention = MakeConvention();
        var chosenValues = new Dictionary<int, string> { { 101, "P01" } }; // Role (102) missing

        var act = () => _useCase.Compose(convention, chosenValues, "0001");

        act.Should().Throw<AttributeValidationException>().WithMessage("*Role*");
    }

    [Fact]
    public void Compose_WithMultipleMissingRequiredFields_NamesAllOfThemInOneException()
    {
        var convention = MakeConvention();
        var chosenValues = new Dictionary<int, string>(); // both Project and Role missing

        var act = () => _useCase.Compose(convention, chosenValues, "0001");

        act.Should().Throw<AttributeValidationException>()
            .WithMessage("*Project*")
            .WithMessage("*Role*");
    }

    [Fact]
    public void Compose_WithOptionalFieldMissing_ProducesEmptySegmentWithoutThrowing()
    {
        var convention = new NamingConventionDto(
            "std1", "Standard", "-",
            Fields: new List<NamingConventionFieldDto>
            {
                Field(101, "Project"),
                Field(104, "Volume", optional: true, position: 1),
                Field(103, "Number", position: 2)
            },
            Metadata: new List<MetadataFieldDisplayDto>());
        var chosenValues = new Dictionary<int, string> { { 101, "P01" } };

        var number = _useCase.Compose(convention, chosenValues, "0001");

        number.Value.Should().Be("P01--0001");
    }

    [Fact]
    public void Compose_WithValueContainingDelimiter_ThrowsAttributeValidationException()
    {
        var convention = MakeConvention();
        var chosenValues = new Dictionary<int, string> { { 101, "P-01" }, { 102, "AR" } };

        var act = () => _useCase.Compose(convention, chosenValues, "0001");

        act.Should().Throw<AttributeValidationException>().WithMessage("*Project*");
    }

    [Fact]
    public void Compose_TrimsChosenValues()
    {
        var convention = MakeConvention();
        var chosenValues = new Dictionary<int, string> { { 101, "  P01  " }, { 102, "AR" } };

        var number = _useCase.Compose(convention, chosenValues, "0001");

        number.Value.Should().Be("P01-AR-0001");
    }

    #endregion

    #region AllocateNextAsync

    [Fact]
    public async Task AllocateNextAsync_WithNoDisciplineFolderYet_StartsAtFirstSequence()
    {
        var convention = MakeConvention();
        var discipline = MakeDiscipline(folder: null);
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var number = await _useCase.AllocateNextAsync(
            "b.project1", discipline, convention, chosenValues, "token", CancellationToken.None);

        number.Sequence.Should().Be("0001");
        await _dataManagementService.DidNotReceive().GetFolderItemsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllocateNextAsync_WithExistingNumbers_AllocatesMaxPlusOne()
    {
        var convention = MakeConvention();
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };
        var discipline = MakeDiscipline(folder);
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var items = new List<ItemDto>
        {
            FileItem("urn:1", "P01-AR-0001.docx"),
            FileItem("urn:2", "P01-AR-0003.docx")
        };
        _dataManagementService.GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);
        _dataManagementService.GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemCustomAttributesResultDto>
            {
                new(Urn: "urn:1", Name: "P01-AR-0001.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?> { { "Project", "P01" }, { "Role", "AR" }, { "Number", "0001" } }),
                new(Urn: "urn:2", Name: "P01-AR-0003.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?> { { "Project", "P01" }, { "Role", "AR" }, { "Number", "0003" } })
            });

        var number = await _useCase.AllocateNextAsync(
            "b.project1", discipline, convention, chosenValues, "token", CancellationToken.None);

        // Never fills the gap at 0002 - always max + 1.
        number.Sequence.Should().Be("0004");
    }

    [Fact]
    public async Task AllocateNextAsync_WidensSequenceWidthToMatchExistingWiderNumbers()
    {
        var convention = MakeConvention();
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };
        var discipline = MakeDiscipline(folder);
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var items = new List<ItemDto> { FileItem("urn:1", "P01-AR-00001.docx") };
        _dataManagementService.GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);
        _dataManagementService.GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemCustomAttributesResultDto>
            {
                new(Urn: "urn:1", Name: "P01-AR-00001.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?> { { "Project", "P01" }, { "Role", "AR" }, { "Number", "00001" } })
            });

        var number = await _useCase.AllocateNextAsync(
            "b.project1", discipline, convention, chosenValues, "token", CancellationToken.None);

        number.Sequence.Should().Be("00002"); // 5-digit width preserved, not narrowed back to 4
    }

    [Fact]
    public async Task AllocateNextAsync_IgnoresFilesWithNoRecognisableDocumentNumber()
    {
        var convention = MakeConvention();
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };
        var discipline = MakeDiscipline(folder);
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        // "site-plan-notes.docx" has 3 hyphen-separated parts too, but attributes carry no values,
        // and the filename fallback is a coincidental 3-part name unrelated to the convention.
        var items = new List<ItemDto> { FileItem("urn:1", "site-plan-notes.docx") };
        _dataManagementService.GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);
        _dataManagementService.GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemCustomAttributesResultDto>
            {
                new(Urn: "urn:1", Name: "site-plan-notes.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?>())
            });

        var number = await _useCase.AllocateNextAsync(
            "b.project1", discipline, convention, chosenValues, "token", CancellationToken.None);

        // Falls back to filename parsing, which DOES accept "site-plan-notes" (3 parts matching the
        // 3-field convention) since attributes carried nothing - this documents that fallback's
        // deliberate strictness is only about segment COUNT, not content.
        number.Sequence.Should().Be("0001");
    }

    #endregion

    #region CheckAvailabilityAsync

    [Fact]
    public async Task CheckAvailabilityAsync_WithNoConflict_ReturnsAvailable()
    {
        var convention = MakeConvention();
        var discipline = MakeDiscipline(folder: null);
        var proposed = _useCase.Compose(convention, new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } }, "0001");

        var result = await _useCase.CheckAvailabilityAsync(
            "b.project1", discipline, convention, proposed, "token", CancellationToken.None);

        result.IsAvailable.Should().BeTrue();
        result.ConflictingFileName.Should().BeNull();
        result.SuggestedNumber.Should().BeNull();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WithConflict_ReturnsUnavailableAndSuggestsNextNumber()
    {
        var convention = MakeConvention();
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };
        var discipline = MakeDiscipline(folder);
        var proposed = _useCase.Compose(convention, new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } }, "0001");

        var items = new List<ItemDto> { FileItem("urn:1", "P01-AR-0001.docx") };
        _dataManagementService.GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);
        _dataManagementService.GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemCustomAttributesResultDto>
            {
                new(Urn: "urn:1", Name: "P01-AR-0001.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?> { { "Project", "P01" }, { "Role", "AR" }, { "Number", "0001" } })
            });

        var result = await _useCase.CheckAvailabilityAsync(
            "b.project1", discipline, convention, proposed, "token", CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
        result.ConflictingFileName.Should().Be("P01-AR-0001.docx");
        result.SuggestedNumber.Should().Be("P01-AR-0002");
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ConflictMatchIsCaseInsensitive()
    {
        var convention = MakeConvention();
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };
        var discipline = MakeDiscipline(folder);
        var proposed = _useCase.Compose(convention, new Dictionary<int, string> { { 101, "p01" }, { 102, "ar" } }, "0001");

        var items = new List<ItemDto> { FileItem("urn:1", "P01-AR-0001.docx") };
        _dataManagementService.GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);
        _dataManagementService.GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<ItemCustomAttributesResultDto>
            {
                new(Urn: "urn:1", Name: "P01-AR-0001.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?> { { "Project", "P01" }, { "Role", "AR" }, { "Number", "0001" } })
            });

        var result = await _useCase.CheckAvailabilityAsync(
            "b.project1", discipline, convention, proposed, "token", CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAvailabilityAsync_WhenTaken_SuggestsTheNextNumberWithinTheSameGroup()
    {
        // The conflicting file's group tops out at 0001; a higher sequence under a different prefix
        // must not push the suggestion past this group's own next free number.
        var convention = MakeConvention();
        var proposed = _useCase.Compose(convention, new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } }, "0001");

        var discipline = StubFolderWithNumbers("P01-AR-0001.docx", "P01-ST-0009.docx");

        var result = await _useCase.CheckAvailabilityAsync(
            "b.project1", discipline, convention, proposed, "token", CancellationToken.None);

        result.IsAvailable.Should().BeFalse();
        result.SuggestedNumber.Should().Be("P01-AR-0002");
    }

    #endregion

    #region AllocateAndVerifyNextAsync

    /// <summary>
    /// Stubs a discipline folder containing files that carry the given sequence numbers.
    /// </summary>
    private DisciplineFolderDto StubFolderWithSequences(params string[] sequences)
    {
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };

        var items = sequences
            .Select((seq, i) => FileItem($"urn:{i}", $"P01-AR-{seq}.docx"))
            .ToList();

        _dataManagementService
            .GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);

        _dataManagementService
            .GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(sequences
                .Select((seq, i) => new ItemCustomAttributesResultDto(
                    Urn: $"urn:{i}", Name: $"P01-AR-{seq}.docx", Title: null, LastModifiedTime: null,
                    LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                    PathInProject: null, WebViewLink: null,
                    Attributes: new Dictionary<string, string?>
                    {
                        { "Project", "P01" }, { "Role", "AR" }, { "Number", seq }
                    }))
                .ToList());

        return MakeDiscipline(folder);
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_ScansTheFolderExactlyOnce()
    {
        var discipline = StubFolderWithSequences("0001", "0002");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        allocation.Number.Value.Should().Be("P01-AR-0003");
        allocation.IsUsable.Should().BeTrue();

        // The reason this method exists. Allocating and then separately checking availability listed
        // the folder and batch-read every file attribute twice in immediate succession, with nothing
        // in between - so the second scan could only ever see what the first had just seen. On a
        // 28-file discipline folder that was the single largest component of a ~30s creation.
        await _dataManagementService.Received(1).GetFolderItemsAsync(
            "b.project1", "folder1", "token", Arg.Any<CancellationToken>());
        await _dataManagementService.Received(1).GetItemCustomAttributesAsync(
            "b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_DoesNotRequestPerFilePathMetadata()
    {
        var discipline = StubFolderWithSequences("0001");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        // This scan only reads attribute values. Asking for paths as well would add one HTTP request
        // per file in the folder - which is what it used to do, for two fields it then discarded.
        await _dataManagementService.Received(1).GetItemCustomAttributesAsync(
            "b.project1", Arg.Any<IEnumerable<string>>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithReservedNumbers_StepsPastThemEvenWhenTheScanCannotSeeThem()
    {
        // The folder listing still shows only 0001: ACC indexes a new version a moment after the
        // Data Management API creates it, so a file written seconds ago is not reliably visible yet.
        var discipline = StubFolderWithSequences("0001");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues,
            ["P01-AR-0002", "P01-AR-0003"], "token");

        // Without the reserved list a batch of several documents could allocate the same number more
        // than once - this is the correctness that is now independent of how fast ACC catches up.
        allocation.Number.Value.Should().Be("P01-AR-0004");
        allocation.IsUsable.Should().BeTrue();
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithAnEmptyFolder_StartsAtTheFirstSequence()
    {
        var discipline = StubFolderWithSequences();
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        allocation.Number.Value.Should().Be("P01-AR-0001");
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithNoFolderYet_AllocatesWithoutAnyRequests()
    {
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", MakeDiscipline(folder: null), MakeConvention(), chosenValues, [], "token");

        // A discipline used for the first time has no folder to scan - the folder is created on
        // demand - so this must not cost a round trip either.
        allocation.Number.Value.Should().Be("P01-AR-0001");
        await _dataManagementService.DidNotReceive().GetFolderItemsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithReservedNumbersOnAnEmptyFolder_StillStepsPastThem()
    {
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", MakeDiscipline(folder: null), MakeConvention(), chosenValues,
            ["P01-AR-0001"], "token");

        // The case that matters for the very first batch in a brand-new discipline: nothing exists in
        // ACC yet, so only the reserved list stops every item being allocated 0001.
        allocation.Number.Value.Should().Be("P01-AR-0002");
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_PreservesAWiderExistingSequenceWidth()
    {
        var discipline = StubFolderWithSequences("000001");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        // Unchanged behaviour, asserted through the new entry point: a project already using wider
        // sequences keeps that width.
        allocation.Number.Value.Should().Be("P01-AR-000002");
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithBlankReservedEntries_IgnoresThem()
    {
        var discipline = StubFolderWithSequences("0001");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues,
            ["", "   ", "P01-AR-0005"], "token");

        allocation.Number.Value.Should().Be("P01-AR-0006");
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithNullReservedList_Throws()
    {
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        await Assert.ThrowsAsync<ArgumentNullException>(() => _useCase.AllocateAndVerifyNextAsync(
            "b.project1", MakeDiscipline(), MakeConvention(), chosenValues, null!, "token"));
    }

    [Fact]
    public async Task AllocateNextAsync_StillDelegatesToTheSingleScanAllocator()
    {
        var discipline = StubFolderWithSequences("0001", "0002");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var number = await _useCase.AllocateNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, "token");

        // Kept for callers that only want a number. One scan, same answer.
        number.Value.Should().Be("P01-AR-0003");
        await _dataManagementService.Received(1).GetFolderItemsAsync(
            "b.project1", "folder1", "token", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Stubs a discipline folder whose files carry arbitrary full numbers and extensions, letting a
    /// test mix numbering groups (prefixes) and file types in a single folder.
    /// </summary>
    private DisciplineFolderDto StubFolderWithNumbers(params string[] fullFileNames)
    {
        var folder = new FolderDto { Id = "folder1", Name = "AR Architect" };

        var items = fullFileNames
            .Select((name, i) => FileItem($"urn:{i}", name))
            .ToList();

        _dataManagementService
            .GetFolderItemsAsync("b.project1", "folder1", "token", Arg.Any<CancellationToken>())
            .Returns(items);

        _dataManagementService
            .GetItemCustomAttributesAsync("b.project1", Arg.Any<IEnumerable<string>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(fullFileNames
                .Select((name, i) =>
                {
                    // Strip the extension the same way the production filename fallback does, without
                    // needing a System.IO import in the test.
                    var dot = name.LastIndexOf('.');
                    var stem = dot > 0 ? name[..dot] : name;
                    var parts = stem.Split('-', StringSplitOptions.TrimEntries);
                    return new ItemCustomAttributesResultDto(
                        Urn: $"urn:{i}", Name: name, Title: null, LastModifiedTime: null,
                        LastModifiedUserName: null, RevisionNumber: null, ApprovalStatus: null,
                        PathInProject: null, WebViewLink: null,
                        Attributes: new Dictionary<string, string?>
                        {
                            { "Project", parts.Length > 0 ? parts[0] : string.Empty },
                            { "Role", parts.Length > 1 ? parts[1] : string.Empty },
                            { "Number", parts.Length > 2 ? parts[2] : string.Empty }
                        });
                })
                .ToList());

        return MakeDiscipline(folder);
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithMixedGroupsInOneFolder_AllocatesWithinTheMatchingGroupOnly()
    {
        // The folder holds three numbering families. The new file is in the P01-AR group, whose own
        // maximum is 0002 - the higher sequences under other prefixes must not drag it up.
        var discipline = StubFolderWithNumbers(
            "P01-AR-0001.docx",
            "P01-AR-0002.docx",
            "P01-ST-0009.docx",
            "P02-AR-0007.docx");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        allocation.Number.Value.Should().Be("P01-AR-0003");
        allocation.IsUsable.Should().BeTrue();
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithAGroupNotYetPresent_StartsAtTheFirstSequence()
    {
        // Only a different group exists, so this group is empty and starts at 0001 - the deliberate
        // change from folder-wide to per-group allocation.
        var discipline = StubFolderWithNumbers("P01-ST-0005.docx");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        allocation.Number.Value.Should().Be("P01-AR-0001");
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_GroupsByNumberStemRegardlessOfFileExtension()
    {
        // Same document number carried by three different file types. The extension is stripped by
        // the filename fallback and is absent from the attribute values, so all three land in one
        // group and the sequence advances past all of them.
        var discipline = StubFolderWithNumbers(
            "P01-AR-0001.docx",
            "P01-AR-0002.pdf",
            "P01-AR-0003.rvt");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues, [], "token");

        allocation.Number.Value.Should().Be("P01-AR-0004");
    }

    [Fact]
    public async Task AllocateAndVerifyNextAsync_WithReservedNumbersInAnotherGroup_DoesNotAdvanceThisGroup()
    {
        // A reserved number from a different prefix must not leak into this group's sequence; only
        // the reserved number sharing the new file's prefix moves it.
        var discipline = StubFolderWithNumbers("P01-AR-0001.docx");
        var chosenValues = new Dictionary<int, string> { { 101, "P01" }, { 102, "AR" } };

        var allocation = await _useCase.AllocateAndVerifyNextAsync(
            "b.project1", discipline, MakeConvention(), chosenValues,
            ["P01-AR-0002", "P01-ST-0009"], "token");

        allocation.Number.Value.Should().Be("P01-AR-0003");
    }

    #endregion
}
