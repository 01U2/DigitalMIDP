using System.Text.Json.Nodes;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Application.UseCases;

namespace DigitalMIDP.Infrastructure.DataManagement;

internal sealed class ApsInventoryReconciler(HttpClient client) : ApsInventoryReader(client), IProjectInventoryReconciler
{
    public async Task<InventoryReconciliation> ReconcileAsync(string hubId, string projectId, ProjectManifest? previous,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        cancellationToken.ThrowIfCancellationRequested();
        if (previous != null && (previous.HubId != hubId || previous.ProjectId != projectId))
            throw new ArgumentException("Previous manifest belongs to another project or hub.", nameof(previous));
        var oldJson = previous == null ? null : ProjectManifestSerializer.Serialize(previous with { Folders = Ordered(previous.Folders) });
        var previousLinks = previous?.Folders.SelectMany(f => f.Files).Select(f => f.Item)
            .ToDictionary(i => (i.Id, i.TipVersionUrn, i.Name, i.ParentId, i.PathInProject), i => i.WebViewLink);
        var root = await ReadRootAsync(hubId, projectId, cancellationToken);
        var queue = new Queue<(ItemDto Folder, string Path, string? Parent)>();
        queue.Enqueue((root, root.Name, null));
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var folders = new List<ManifestFolder>();
        var docsProject = DocsProject(projectId);
        var projectSchema = await ReadDefinitionsAsync(docsProject, null, cancellationToken);
        while (queue.TryDequeue(out var entry))
        {
            var folder = entry.Folder;
            var items = await ReadChildrenAsync(projectId, folder.Id, cancellationToken);
            var files = new List<ItemDto>();
            foreach (var item in items)
            {
                if (!seen.Add(item.Id)) throw new InvalidDataException("Duplicate APS item or folder in inventory: " + item.Id);
                if (item.Type == ItemType.File)
                {
                    if (!ProjectManifest.IsReserved(item.Name)) files.Add(item);
                }
                else if (!FolderNameMatcher.IsHiddenAdminFolder(item.Name)) queue.Enqueue((item, entry.Path + "/" + item.Name, folder.Id));
            }
            var schema = MergeDefinitions(projectSchema, await ReadDefinitionsAsync(docsProject, folder.Id, cancellationToken));
            var names = schema.ToDictionary(d => d.AttributeId, d => d.Name);
            var enriched = new List<ManifestFile>();
            foreach (var batch in files.Chunk(500))
            {
                var values = await ReadBatchAsync(docsProject, batch, schema, cancellationToken);
                foreach (var item in batch)
                {
                    var attributes = values[item.Id];
                    var link = item.WebViewLink;
                    if (link == null && (previousLinks == null || !previousLinks.TryGetValue(
                        (item.Id, item.TipVersionUrn, item.Name, folder.Id, entry.Path), out link)))
                        link = await ReadWebLinkAsync(projectId, item, cancellationToken);
                    enriched.Add(new ManifestFile(item with
                    {
                        ParentId = folder.Id, Location = entry.Path, PathInProject = entry.Path,
                        WebViewLink = link,
                        LifecycleStage = FolderNameMatcher.ClassifyLifecycleStage(entry.Path),
                        CustomAttributes = attributes.ToDictionary(a => names[a.Key], a => a.Value, StringComparer.OrdinalIgnoreCase)
                    }, attributes));
                }
            }
            folders.Add(new ManifestFolder(folder.Id, folder.Name, entry.Parent, entry.Path, schema, enriched));
        }
        var candidate = new ProjectManifest(1, hubId, projectId, previous?.Revision ?? 1,
            previous?.PublishedAtUtc ?? DateTimeOffset.UtcNow, Ordered(folders));
        var json = ProjectManifestSerializer.Serialize(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        if (oldJson != null && JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(oldJson))) return new(false, previous!);
        return new(true, candidate with { Revision = checked((previous?.Revision ?? 0) + 1), PublishedAtUtc = DateTimeOffset.UtcNow });
    }

    private static ManifestFolder[] Ordered(IEnumerable<ManifestFolder> folders) => folders.OrderBy(f => f.FolderId, StringComparer.Ordinal)
        .Select(f => f with { Files = f.Files.OrderBy(i => i.Item.Id, StringComparer.Ordinal).ToArray(),
            Definitions = f.Definitions.OrderBy(d => d.AttributeId).ToArray() }).ToArray();
}
