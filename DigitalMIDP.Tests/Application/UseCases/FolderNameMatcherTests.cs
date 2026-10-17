using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.UseCases;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

/// <summary>
/// Unit tests for FolderNameMatcher, the human-tolerant folder-name matching that underpins
/// resolving WIP/Shared/Published/Archive and discipline folders for file creation from template.
/// </summary>
public sealed class FolderNameMatcherTests
{
    private static FolderDto Folder(string name) => new() { Id = Guid.NewGuid().ToString(), Name = name };

    #region Normalize

    [Theory]
    [InlineData("WIP", "wip")]
    [InlineData("wip", "wip")]
    [InlineData("01 - WIP", "01wip")]
    [InlineData("Work In Progress", "workinprogress")]
    [InlineData("AR Architect", "ararchitect")]
    [InlineData("AR-Architect", "ararchitect")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_StripsSeparatorsAndCase(string? input, string expected)
    {
        FolderNameMatcher.Normalize(input).Should().Be(expected);
    }

    #endregion

    #region FindByName

    [Fact]
    public void FindByName_WithExactNormalisedMatch_ReturnsIt()
    {
        var folders = new[] { Folder("01 - WIP"), Folder("Shared") };

        var result = FolderNameMatcher.FindByName(folders, ["wip", "workinprogress", "01wip"]);

        result.Should().NotBeNull();
        result!.Name.Should().Be("01 - WIP");
    }

    [Fact]
    public void FindByName_PrefersExactMatchOverPrefixMatch()
    {
        var folders = new[] { Folder("WIP Models"), Folder("WIP") };

        var result = FolderNameMatcher.FindByName(folders, ["wip"]);

        result!.Name.Should().Be("WIP");
    }

    [Fact]
    public void FindByName_FallsBackToPrefixMatch()
    {
        var folders = new[] { Folder("WIP Models") };

        var result = FolderNameMatcher.FindByName(folders, ["wip"]);

        result!.Name.Should().Be("WIP Models");
    }

    [Fact]
    public void FindByName_WithNoMatch_ReturnsNull()
    {
        var folders = new[] { Folder("Archive") };

        var result = FolderNameMatcher.FindByName(folders, ["wip"]);

        result.Should().BeNull();
    }

    [Fact]
    public void FindByName_DoesNotMatchSubstringInMiddleOfName()
    {
        // "Archive" should not claim a folder like "The Archived Drawings" via prefix matching,
        // since the accepted token does not appear at the start of the normalised name.
        var folders = new[] { Folder("The Archived Drawings") };

        var result = FolderNameMatcher.FindByName(folders, ["archive", "archived"]);

        result.Should().BeNull();
    }

    [Fact]
    public void FindByName_WithEmptyFolderList_ReturnsNull()
    {
        var result = FolderNameMatcher.FindByName(Array.Empty<FolderDto>(), ["wip"]);

        result.Should().BeNull();
    }

    #endregion

    #region FindDisciplineFolder

    [Fact]
    public void FindDisciplineFolder_MatchesConventionalCodeAndDescriptionForm()
    {
        var folders = new[] { Folder("AR Architect"), Folder("ST Structural") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "AR", "Architect", "AR Architect");

        result!.Name.Should().Be("AR Architect");
    }

    [Fact]
    public void FindDisciplineFolder_FallsBackToBareCode()
    {
        var folders = new[] { Folder("AR") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "AR", "Architect", "AR Architect");

        result!.Name.Should().Be("AR");
    }

    [Fact]
    public void FindDisciplineFolder_FallsBackToDescriptionAlone()
    {
        var folders = new[] { Folder("Architect") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "AR", "Architect", "AR Architect");

        result!.Name.Should().Be("Architect");
    }

    [Fact]
    public void FindDisciplineFolder_FallsBackToCodeAsLeadingToken()
    {
        var folders = new[] { Folder("AR Architectural Drawings") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "AR", "Architect", "AR Architect");

        result!.Name.Should().Be("AR Architectural Drawings");
    }

    [Fact]
    public void FindDisciplineFolder_WithSingleCharacterCode_DoesNotUseLeadingTokenFallback()
    {
        // A single-letter code as a prefix would match almost any folder, so that fallback tier is
        // guarded to codes of two or more characters.
        var folders = new[] { Folder("Anything Else") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "A", "Architect", "A Architect");

        result.Should().BeNull();
    }

    [Fact]
    public void FindDisciplineFolder_WithNoCandidates_ReturnsNull()
    {
        var result = FolderNameMatcher.FindDisciplineFolder(Array.Empty<FolderDto>(), "AR", "Architect", "AR Architect");

        result.Should().BeNull();
    }

    [Fact]
    public void FindDisciplineFolder_WithNoMatchingFolder_ReturnsNull()
    {
        var folders = new[] { Folder("ST Structural") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "AR", "Architect", "AR Architect");

        result.Should().BeNull();
    }

    [Fact]
    public void FindDisciplineFolder_WithNullDescription_StillMatchesByCode()
    {
        var folders = new[] { Folder("AR") };

        var result = FolderNameMatcher.FindDisciplineFolder(folders, "AR", null, "AR");

        result!.Name.Should().Be("AR");
    }

    #endregion

    #region ClassifyLifecycleStage

    [Theory]
    [InlineData("Project Files/WIP/AR Architect/File.rvt", CdeLifecycleStage.Wip)]
    [InlineData("Project Files/01 - WIP/AR Architect", CdeLifecycleStage.Wip)]
    [InlineData("Project Files/Shared/AR Architect", CdeLifecycleStage.Shared)]
    [InlineData("Project Files/Published/AR Architect", CdeLifecycleStage.Published)]
    [InlineData("Project Files/Archive/Old", CdeLifecycleStage.Archive)]
    public void ClassifyLifecycleStage_RecognisesEachStage(string path, CdeLifecycleStage expected)
    {
        FolderNameMatcher.ClassifyLifecycleStage(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Project Files/Some Other Folder")]
    public void ClassifyLifecycleStage_WithUnmatchedOrEmptyPath_ReturnsNull(string? path)
    {
        FolderNameMatcher.ClassifyLifecycleStage(path).Should().BeNull();
    }

    #endregion
}
