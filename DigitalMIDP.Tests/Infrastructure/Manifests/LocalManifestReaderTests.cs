using System.IO;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Manifests;

public sealed class LocalManifestReaderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly LocalManifestProject project = new("h", "p", "Hub", "Project");
    private readonly ServiceProvider provider;
    private readonly ILocalManifestReader reader;
    private string Admin => Path.Combine(root, "Hub", "Project", "Project Files", "00 - PROJECT Files");
    private string Templates => Path.Combine(Admin, "Project-Template");
    private string Manifest => Path.Combine(Templates, ProjectManifest.FileName);

    public LocalManifestReaderTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Autodesk:ClientId"] = "offline", ["Autodesk:RedirectUri"] = "http://localhost",
            ["DesktopConnector:SyncRootPath"] = root
        }).Build();
        provider = new ServiceCollection().AddApsServices(config).BuildServiceProvider();
        reader = provider.GetRequiredService<ILocalManifestReader>();
        Directory.CreateDirectory(Admin);
    }

    private static string Json(long revision, string hub = "h", string projectId = "p", int minute = 0) =>
        ProjectManifestSerializer.Serialize(new(ProjectManifest.CurrentSchemaVersion, hub, projectId, revision,
            DateTimeOffset.UnixEpoch.AddMinutes(minute), []));

    [Fact]
    public async Task OnlyValidatedNonRegressingRevisionsReplaceTheProjectSnapshot()
    {
        Directory.CreateDirectory(Templates);
        await File.WriteAllTextAsync(Manifest, Json(4));
        Assert.Equal(LocalManifestStatus.Available, (await reader.ReadAsync(project)).Status);
        foreach (var invalid in new[] { "{", "{\"\\uD800\":1}", Json(5, "other"), Json(5, projectId: "other"), Json(3), Json(4, minute: 1) })
        {
            await File.WriteAllTextAsync(Manifest, invalid);
            var snapshot = await reader.ReadAsync(project);
            Assert.Equal(LocalManifestStatus.Stale, snapshot.Status);
            Assert.Equal(4, snapshot.Manifest!.Revision);
            Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.Manifest.PublishedAtUtc);
        }
        File.Delete(Manifest);
        Assert.Equal(LocalManifestStatus.Stale, (await reader.ReadAsync(project)).Status);
        foreach (var other in new[] { project with { HubId = "other" }, project with { ProjectId = "other" } })
        {
            var missing = await reader.ReadAsync(other);
            Assert.Equal(LocalManifestStatus.Unavailable, missing.Status);
            Assert.Null(missing.Manifest);
        }
        await File.WriteAllTextAsync(Manifest, Json(8));
        Assert.Equal(8, (await reader.ReadAsync(project)).Manifest!.Revision);
    }

    [Fact]
    public async Task RejectsTraversalAndAmbiguousAncestorEvenIfOnlyOneContainsManifest()
    {
        Directory.CreateDirectory(Templates);
        await File.WriteAllTextAsync(Manifest, Json(1));
        Assert.Null((await reader.ReadAsync(project with { ProjectName = @"..\Hub\Project" })).Manifest);
        Assert.Null((await reader.ReadAsync(project with { HubName = Path.Combine(root, "Hub") })).Manifest);
        var duplicate = Directory.CreateDirectory(Path.Combine(Admin, "Templates"));
        Assert.Null((await reader.ReadAsync(project)).Manifest);
        duplicate.Delete();
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Admin)!, "00.ProjectFiles"));
        Assert.Null((await reader.ReadAsync(project)).Manifest);
    }

    [Fact]
    public async Task LockedManifestRetainsLastRevisionUntilReadable()
    {
        Directory.CreateDirectory(Templates);
        await File.WriteAllTextAsync(Manifest, Json(1));
        Assert.Equal(LocalManifestStatus.Available, (await reader.ReadAsync(project)).Status);
        using (File.Open(Manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(LocalManifestStatus.Stale, (await reader.ReadAsync(project)).Status);
        Assert.Equal(LocalManifestStatus.Available, (await reader.ReadAsync(project)).Status);
    }

    [Fact]
    public async Task OfflineFileIsNotAcceptedUntilHydrated()
    {
        Directory.CreateDirectory(Templates);
        await File.WriteAllTextAsync(Manifest, Json(1));
        var attributes = File.GetAttributes(Manifest);
        File.SetAttributes(Manifest, attributes | FileAttributes.Offline);
        try { Assert.Equal(LocalManifestStatus.Unavailable, (await reader.ReadAsync(project)).Status); }
        finally { File.SetAttributes(Manifest, attributes); }
        Assert.Equal(LocalManifestStatus.Available, (await reader.ReadAsync(project)).Status);
    }

    [Fact]
    public async Task WatchFollowsLateTemplatesPartialWriteAndAtomicReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var updates = reader.WatchAsync(project, timeout.Token).GetAsyncEnumerator();
        async Task Expect(LocalManifestStatus status, long? revision)
        {
            Assert.True(await updates.MoveNextAsync());
            Assert.Equal(status, updates.Current.Status);
            Assert.Equal(revision, updates.Current.Manifest?.Revision);
        }
        await Expect(LocalManifestStatus.Unavailable, null);
        Directory.CreateDirectory(Templates);
        await File.WriteAllTextAsync(Manifest, Json(1));
        await Expect(LocalManifestStatus.Available, 1);
        await File.WriteAllTextAsync(Manifest, "{");
        await Expect(LocalManifestStatus.Stale, 1);
        await File.WriteAllTextAsync(Manifest + ".tmp", Json(2));
        File.Move(Manifest + ".tmp", Manifest, overwrite: true);
        await Expect(LocalManifestStatus.Available, 2);
        var waiting = updates.MoveNextAsync().AsTask();
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task DisposingAnEnumerationAndCancellingAReadReleaseResources()
    {
        await using (var updates = reader.WatchAsync(project).GetAsyncEnumerator())
            Assert.True(await updates.MoveNextAsync());
        Directory.Delete(root, true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(project, new CancellationToken(true)));
    }

    public void Dispose()
    {
        provider.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
