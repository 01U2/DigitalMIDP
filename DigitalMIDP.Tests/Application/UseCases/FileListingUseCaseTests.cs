using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

/// <summary>
/// Tests for <see cref="FileListingUseCase"/>, with the emphasis on what it does with APS data that
/// is not shaped the way it should be.
///
/// <para>
/// This matters more here than almost anywhere else in the application: the result of this walk IS
/// the files register the user reads. A malformed entry carried through becomes a row that cannot be
/// opened or edited, and a folder graph that is not a tree becomes either duplicate rows filed under
/// different paths or a stack overflow.
/// </para>
/// </summary>
public sealed class FileListingUseCaseTests
{
    private readonly IDataManagementService _dataManagementService = Substitute.For<IDataManagementService>();

    private FileListingUseCase CreateUseCase() => new(
        _dataManagementService,
        Substitute.For<ILogger<FileListingUseCase>>());

    private void StubRoot(string id = "root", string name = "Project Files") =>
        _dataManagementService
            .GetRootFolderAsync("hub1", "b.p1", "token", Arg.Any<CancellationToken>())
            .Returns(new FolderDto { Id = id, Name = name });

    private void StubFolder(string folderId, params ItemDto[] items) =>
        _dataManagementService
            .GetFolderItemsAsync("b.p1", folderId, "token", Arg.Any<CancellationToken>())
            .Returns(items);

    private static ItemDto File(string id, string name) => new()
    {
        Id = id, Name = name, Type = ItemType.File, ItemUrn = id
    };

    private static ItemDto Folder(string id, string name) => new()
    {
        Id = id, Name = name, Type = ItemType.Folder
    };

    private Task<GetFolderItemsResponseDto> ListAll() =>
        CreateUseCase().GetAllProjectFilesAsync("hub1", "b.p1", "token", CancellationToken.None);

    [Fact]
    public async Task GetAllProjectFilesAsync_BuildsEachFilesLocationFromThePathWalked()
    {
        StubRoot();
        StubFolder("root", Folder("wip", "01 WIP"));
        StubFolder("wip", Folder("ar", "AR Architect"));
        StubFolder("ar", File("f1", "P01-AR-0001.docx"));

        var result = await ListAll();

        var file = result.Items.Should().ContainSingle().Subject;
        file.Location.Should().Be("Project Files/01 WIP/AR Architect");

        // ParentId comes from the folder actually walked, because the Autodesk item API does not
        // return attributes.parentId at all.
        file.ParentId.Should().Be("ar");
        file.LifecycleStage.Should().Be(CdeLifecycleStage.Wip);
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_WithNoUsableRootFolder_FailsLoudly()
    {
        _dataManagementService
            .GetRootFolderAsync("hub1", "b.p1", "token", Arg.Any<CancellationToken>())
            .Returns(new FolderDto { Id = "", Name = "Project Files" });

        var act = ListAll;

        // Reporting an empty project would be the worst outcome for a files register: it reads as
        // data loss rather than as a failure, and the user has no way to tell the difference.
        await act.Should().ThrowAsync<DigitalMIDP.Application.Exceptions.ApplicationException>()
            .WithMessage("*no usable root folder*");
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_SkipsEntriesWithNoId()
    {
        StubRoot();
        StubFolder("root",
            File("f1", "Good.docx"),
            new ItemDto { Id = "", Name = "NoId.docx", Type = ItemType.File });

        var result = await ListAll();

        // An entry with no id cannot be opened, cannot have its attributes written, and cannot be
        // matched back to anything in ACC.
        result.Items.Should().ContainSingle().Which.Name.Should().Be("Good.docx");
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_SkipsEntriesWithNoName()
    {
        StubRoot();
        StubFolder("root",
            File("f1", "Good.docx"),
            new ItemDto { Id = "f2", Name = "   ", Type = ItemType.File });

        var result = await ListAll();

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Good.docx");
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_DoesNotRecurseIntoAFolderWithNoId()
    {
        StubRoot();
        StubFolder("root",
            new ItemDto { Id = "", Name = "Broken Folder", Type = ItemType.Folder },
            File("f1", "Good.docx"));

        var result = await ListAll();

        result.Items.Should().ContainSingle();

        // Recursing on a blank id is the dangerous case: the request would resolve to some other
        // folder and file its contents under this one's path.
        await _dataManagementService.DidNotReceive().GetFolderItemsAsync(
            "b.p1", "", "token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_WithAFolderCycle_TerminatesAndListsEachFileOnce()
    {
        StubRoot();
        StubFolder("root", Folder("a", "A"));
        StubFolder("a", File("f1", "InA.docx"), Folder("b", "B"));

        // B contains A again - a graph, not a tree. Nothing validates that APS folder listings are
        // acyclic.
        StubFolder("b", File("f2", "InB.docx"), Folder("a", "A"));

        var result = await ListAll();

        // Without the visited set this recurses until the stack overflows, taking the application
        // with it. With it, each file appears exactly once, under the path it was first reached by.
        result.Items.Select(i => i.Name).Should().BeEquivalentTo("InA.docx", "InB.docx");

        await _dataManagementService.Received(1).GetFolderItemsAsync(
            "b.p1", "a", "token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_WithADiamondShapedGraph_DoesNotDuplicateFiles()
    {
        StubRoot();
        StubFolder("root", Folder("x", "X"), Folder("y", "Y"));

        // The same folder reachable by two different paths.
        StubFolder("x", Folder("shared", "Shared"));
        StubFolder("y", Folder("shared", "Shared"));
        StubFolder("shared", File("f1", "Once.docx"));

        var result = await ListAll();

        // The quieter half of the cycle problem: this terminates on its own, but would list the file
        // twice with two different Locations - two rows for one real file.
        result.Items.Should().ContainSingle().Which.Name.Should().Be("Once.docx");
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_WithADeeplyNestedChain_StopsRatherThanRecursingForever()
    {
        StubRoot();

        // 100 distinct nested folders - past the depth limit, and with distinct ids so the visited
        // set does not catch it. A real CDE structure is three or four levels deep.
        StubFolder("root", Folder("d0", "D0"));
        for (var i = 0; i < 100; i++)
        {
            StubFolder($"d{i}", Folder($"d{i + 1}", $"D{i + 1}"));
        }

        var act = ListAll;

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_WithANullItemCollection_DoesNotThrow()
    {
        StubRoot();
        _dataManagementService
            .GetFolderItemsAsync("b.p1", "root", "token", Arg.Any<CancellationToken>())
            .Returns((IEnumerable<ItemDto>?)null!);

        var result = await ListAll();

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_StillListsAFileWithNoItemUrn()
    {
        StubRoot();
        StubFolder("root", new ItemDto
        {
            Id = "f1", Name = "NoUrn.docx", Type = ItemType.File, ItemUrn = null
        });

        var result = await ListAll();

        // Deliberately NOT dropped. The file genuinely exists, so hiding it would be exactly the
        // data loss this validation exists to prevent - it simply cannot show attribute values.
        result.Items.Should().ContainSingle().Which.Name.Should().Be("NoUrn.docx");
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_SkipsTheHiddenAdminFolder()
    {
        StubRoot();
        StubFolder("root", Folder("admin", "00 ProjectFiles"), File("f1", "Real.docx"));
        StubFolder("admin", File("tpl", "Template.docx"));

        var result = await ListAll();

        // Templates and similar are not project deliverables and must never appear as rows,
        // regardless of the user's own ACC permissions on that folder.
        result.Items.Should().ContainSingle().Which.Name.Should().Be("Real.docx");
        await _dataManagementService.DidNotReceive().GetFolderItemsAsync(
            "b.p1", "admin", "token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllProjectFilesAsync_WhenCancelled_Throws()
    {
        StubRoot();
        StubFolder("root", File("f1", "Good.docx"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => CreateUseCase().GetAllProjectFilesAsync("hub1", "b.p1", "token", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("", "b.p1", "token")]
    [InlineData("hub1", "", "token")]
    [InlineData("hub1", "b.p1", "")]
    public async Task GetAllProjectFilesAsync_WithMissingArguments_Throws(
        string hubId, string projectId, string accessToken)
    {
        var act = () => CreateUseCase().GetAllProjectFilesAsync(
            hubId, projectId, accessToken, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithMissingDependencies_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileListingUseCase(
            null!, Substitute.For<ILogger<FileListingUseCase>>()));
        Assert.Throws<ArgumentNullException>(() => new FileListingUseCase(
            _dataManagementService, null!));
    }
}
