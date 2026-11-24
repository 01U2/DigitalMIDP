using System.Text.Json;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Manifests;
using FluentAssertions;
using Xunit;

namespace DigitalMIDP.Tests.Application.Manifests;

public class ProjectManifestSerializerTests
{
    private const string Fixture = """
        {"schemaVersion":1,"hubId":"hub","projectId":"project","revision":7,"publishedAtUtc":"2026-11-22T12:00:00Z","folders":[
          {"folderId":"wip","name":"Design","parentFolderId":"shared","pathInProject":"Project Files/WIP/Design","definitions":[
            {"attributeId":9,"name":"Description","type":"STRING","optional":false,"options":[]},
            {"attributeId":12,"name":"Title","type":"ARRAY","optional":true,"options":[{"value":"A","description":"First"}],"defaultValue":"A"}],
           "files":[{"item":{"id":"i1","parentId":"wip","type":1,"name":"Drawing A.pdf","location":"WIP/Design","pathInProject":"Project Files/WIP/Design","webViewLink":"https://acc.autodesk.com/docs/files?folder=wip","customAttributes":{"Description":"Ground floor","Title":"A"},"tipVersionUrn":"urn:version:1"},"values":{"9":"Ground floor","12":null}}]},
          {"folderId":"shared","name":"WIP","parentFolderId":null,"pathInProject":"Project Files/WIP","definitions":[{"attributeId":9,"name":"Status","type":"STRING","optional":true,"options":[]}],"files":[]}]}
        """;

    private static ProjectManifest Read(string json) => ProjectManifestSerializer.Deserialize(json, "hub", "project", 7);

    [Fact]
    public void V1Fixture_RoundTripsExistingDtosAndFolderScopedIds()
    {
        var manifest = Read(Fixture);
        manifest.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        var child = manifest.Folders[0];
        child.Name.Should().Be("Design");
        child.ParentFolderId.Should().Be(manifest.Folders[1].FolderId);
        child.PathInProject.Should().Be("Project Files/WIP/Design");
        manifest.Folders[1].ParentFolderId.Should().BeNull();
        var file = manifest.Folders[0].Files.Single();
        file.Item.Type.Should().Be(ItemType.File);
        file.Item.CustomAttributes!["Description"].Should().Be("Ground floor");
        file.Item.CustomAttributes["Title"].Should().Be("A");
        file.Item.TipVersionUrn.Should().Be("urn:version:1");
        file.Values[9].Should().Be("Ground floor");
        file.Values[12].Should().BeNull();
        manifest.Folders[0].Definitions[1].Options.Single().Value.Should().Be("A");
        manifest.Folders[1].Definitions.Single().Name.Should().Be("Status");
        Read(ProjectManifestSerializer.Serialize(manifest)).Should().BeEquivalentTo(manifest, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Hub", "project", 7)]
    [InlineData("hub", "other", 7)]
    [InlineData("hub", "project", 6)]
    public void Read_RejectsDifferentRequestedSnapshot(string hubId, string projectId, long revision)
    {
        Action read = () => ProjectManifestSerializer.Deserialize(Fixture, hubId, projectId, revision);
        read.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"schemaVersion\":1,", "")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"revision\":7", "\"revision\":0")]
    [InlineData("\"revision\":7", "\"revision\":null")]
    [InlineData("\"revision\":7", "\"revision\":1,\"revision\":7")]
    [InlineData("\"publishedAtUtc\":\"2026-11-22T12:00:00Z\",", "")]
    [InlineData("2026-11-22T12:00:00Z", "0001-01-01T00:00:00Z")]
    [InlineData("2026-11-22T12:00:00Z", "2026-11-22T12:00:00+01:00")]
    [InlineData("2026-11-22T12:00:00Z", "2026-11-22T12:00:00")]
    [InlineData("\"name\":\"Design\",", "")]
    [InlineData("\"parentFolderId\":null,", "")]
    [InlineData("\"pathInProject\":\"Project Files/WIP\",", "")]
    [InlineData("\"Design\"", "\"NUL\"")]
    [InlineData("\"Project Files/WIP\"", "\"../WIP\"")]
    [InlineData("\"parentFolderId\":\"shared\"", "\"parentFolderId\":\"missing\"")]
    [InlineData("\"parentFolderId\":\"shared\"", "\"parentFolderId\":\"wip\"")]
    [InlineData("\"parentFolderId\":null", "\"parentFolderId\":\"wip\"")]
    [InlineData("\"Project Files/WIP/Design\"", "\"Project Files/Design\"")]
    [InlineData("\"folders\":", "\"unexpected\":")]
    [InlineData("\"files\":[]", "\"files\":null")]
    [InlineData("\"files\":[]", "\"files\":[null]")]
    [InlineData("\"shared\"", "\"wip\"")]
    [InlineData("\"attributeId\":12", "\"attributeId\":9")]
    [InlineData("\"name\":\"Title\"", "\"name\":\"description\"")]
    [InlineData("\"optional\":false,", "")]
    [InlineData("\"options\":[]", "\"options\":null")]
    [InlineData("\"options\":[]", "\"options\":[null]")]
    [InlineData("\"parentId\":\"wip\"", "\"parentId\":\"shared\"")]
    [InlineData("\"type\":1,", "")]
    [InlineData("\"type\":1", "\"type\":17")]
    [InlineData("\"12\":null", "\"13\":null")]
    [InlineData("\"12\":null", "\"012\":null")]
    [InlineData("\"12\":null", "\"9\":null")]
    [InlineData("\"values\":{\"9\":\"Ground floor\",\"12\":null}", "\"values\":null")]
    public void Read_RejectsInvalidV1(string original, string replacement)
    {
        string invalid = Fixture.Replace(original, replacement, StringComparison.Ordinal);
        invalid.Should().NotBe(Fixture);
        Action read = () => ProjectManifestSerializer.Deserialize(invalid, "hub", "project");
        read.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("Drawing A.pdf", "../outside.pdf")]
    [InlineData("Drawing A.pdf", "NUL.pdf")]
    [InlineData("Drawing A.pdf", "x:stream")]
    [InlineData("Drawing A.pdf", "drawing.pdf.")]
    [InlineData("WIP/Design", "WIP/../other")]
    [InlineData("Project Files/WIP/Design", @"C:\outside")]
    [InlineData("Project Files/WIP/Design", @"\\server\share")]
    [InlineData("Project Files/WIP/Design", "/outside")]
    [InlineData("https://acc.autodesk.com/docs/files?folder=wip", "file:///C:/outside")]
    [InlineData("https://acc.autodesk.com/docs/files?folder=wip", "http://acc.autodesk.com/docs")]
    [InlineData("https://acc.autodesk.com/docs/files?folder=wip", "https://acc.autodesk.com.invalid/docs")]
    [InlineData("https://acc.autodesk.com/docs/files?folder=wip", "https://user@acc.autodesk.com/docs")]
    public void Read_RejectsUnsafeUiValues(string original, string replacement)
    {
        var invalid = Fixture.Replace(JsonSerializer.Serialize(original), JsonSerializer.Serialize(replacement), StringComparison.Ordinal);
        invalid.Should().NotBe(Fixture);
        Action read = () => Read(invalid);
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_RejectsDuplicateFilesBeforePublishingJson()
    {
        var manifest = Read(Fixture);
        var folder = manifest.Folders[0];
        var invalid = manifest with { Folders = [folder with { Files = [folder.Files[0], folder.Files[0]] }, manifest.Folders[1]] };
        Action write = () => ProjectManifestSerializer.Serialize(invalid);
        write.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_RejectsMissingParentBeforePublishingJson()
    {
        var manifest = Read(Fixture);
        Action write = () => ProjectManifestSerializer.Serialize(manifest with { Folders = [manifest.Folders[0]] });
        write.Should().Throw<JsonException>();
    }
}
