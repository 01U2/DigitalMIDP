using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Application.UseCases;

namespace DigitalMIDP.Infrastructure.Manifests;

internal sealed partial class LocalManifestReader(string syncRoot) : ILocalManifestReader
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1);
        public string? Json;
        public long Revision;
    }

    private readonly ConcurrentDictionary<(string, string), Entry> entries = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public Task<LocalManifestSnapshot> ReadAsync(LocalManifestProject project, CancellationToken ct = default) =>
        Task.Run(() => ReadCoreAsync(project, null, ct), ct);

    private async Task<LocalManifestSnapshot> ReadCoreAsync(LocalManifestProject project,
        Action<string, Func<string?, bool>>? watch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(project.HubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(project.ProjectId);
        var entry = entries.GetOrAdd((project.HubId, project.ProjectId), _ => new());
        await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = Resolve(project, watch, ct);
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(file, Utf8, detectEncodingFromByteOrderMarks: false);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var manifest = ProjectManifestSerializer.Deserialize(json, project.HubId, project.ProjectId);
            if (manifest.Revision < entry.Revision || (manifest.Revision == entry.Revision && json != entry.Json))
                throw new InvalidDataException("Older or conflicting manifest revision.");
            ct.ThrowIfCancellationRequested();
            entry.Json = json;
            entry.Revision = manifest.Revision;
            return new(LocalManifestStatus.Available, manifest, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or DecoderFallbackException)
        {
            ct.ThrowIfCancellationRequested();
            return entry.Json is null ? new(LocalManifestStatus.Unavailable, null, ex.Message) :
                new(LocalManifestStatus.Stale, ProjectManifestSerializer.Deserialize(entry.Json, project.HubId, project.ProjectId), ex.Message);
        }
        finally { entry.Gate.Release(); }
    }

    private string Resolve(LocalManifestProject project, Action<string, Func<string?, bool>>? watch, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(syncRoot) || syncRoot.StartsWith(@"\\") || syncRoot.StartsWith("//"))
            throw new InvalidDataException("Expected a local absolute ACCDocs root.");
        var root = new DirectoryInfo(syncRoot);
        if (new DriveInfo(root.Root.FullName).DriveType == DriveType.Network)
            throw new InvalidDataException("Expected a local ACCDocs drive.");
        CheckParents(root);
        var directory = root.FullName;
        directory = Child(Exact(project.HubName), true);
        directory = Child(Exact(project.ProjectName), true);
        directory = Child(Exact("Project Files"), true);
        directory = Child(FolderNameMatcher.IsHiddenAdminFolder, true);
        directory = Child(FolderNameMatcher.IsTemplateFolder, true);
        return Child(ProjectManifest.IsReserved, false);

        string Child(Func<string?, bool> matches, bool isDirectory)
        {
            ct.ThrowIfCancellationRequested();
            watch?.Invoke(directory, matches);
            var items = new DirectoryInfo(directory).EnumerateFileSystemInfos().Where(i => matches(i.Name)).Take(2).ToArray();
            if (items.Length != 1 || (items[0] is DirectoryInfo) != isDirectory)
                throw new InvalidDataException($"Missing, ambiguous or unexpected entry in '{directory}'.");
            CheckLocal(items[0]);
            return items[0].FullName;
        }
    }

    private static Func<string?, bool> Exact(string name) => n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase);

    private static void CheckParents(DirectoryInfo dir)
    {
        if (dir.Parent is { } parent) CheckParents(parent);
        CheckLocal(dir);
    }

    private static void CheckLocal(FileSystemInfo item)
    {
        // RECALL_ON_OPEN and RECALL_ON_DATA_ACCESS are cloud placeholder flags not named by FileAttributes.
        const FileAttributes needsHydration = FileAttributes.Offline | (FileAttributes)(0x40000 | 0x400000);
        var attributes = File.GetAttributes(item.FullName);
        if ((attributes & needsHydration) != 0 || ((attributes & FileAttributes.ReparsePoint) != 0 && item.LinkTarget != null))
            throw new InvalidDataException($"'{item.Name}' is not hydrated locally or is a filesystem link.");
    }
}
