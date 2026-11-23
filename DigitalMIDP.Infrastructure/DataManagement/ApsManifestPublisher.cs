using System.Text;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Infrastructure.Http;

namespace DigitalMIDP.Infrastructure.DataManagement;

internal sealed partial class ApsManifestPublisher(HttpClient client, DataManagementService storage,
    IProjectInventoryReconciler reconciler) : ApsInventoryReader(client), IManifestPublisher
{
    // Shared by all transient publishers. APS does not offer an atomic compare-and-swap across clients.
    private static readonly SemaphoreSlim Gate = new(1);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public async Task<ManifestCheckResult> CheckAsync(string hubId, string projectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await Gate.WaitAsync(ct);
        try
        {
            using var requests = new ManifestRequestCounter();
            var folder = await ResolveTemplatesAsync(hubId, projectId, ct);
            var item = await FindManifestAsync(projectId, folder, ct);
            var project = Part(projectId);
            var previous = item == null ? null : ProjectManifestSerializer.Deserialize(
                Utf8.GetString(await storage.DownloadTipAsync(project, item.Id, item.TipVersionUrn, ct)), hubId, projectId);
            var scan = await reconciler.ReconcileAsync(hubId, projectId, previous, ct);
            ct.ThrowIfCancellationRequested();
            if (!scan.Changed)
            {
                if (previous == null) throw new InvalidDataException("Unchanged inventory without a previous manifest.");
                return new(DateTimeOffset.UtcNow, previous.PublishedAtUtc, false, requests.Count);
            }

            var candidate = scan.Manifest;
            if (candidate.ProjectId != projectId || candidate.HubId != hubId || candidate.Revision != checked((previous?.Revision ?? 0) + 1))
                throw new InvalidDataException("Unexpected publication identity or revision.");
            var bytes = Utf8.GetBytes(ProjectManifestSerializer.Serialize(candidate));
            var latest = await FindManifestAsync(projectId, folder, ct);
            if (latest?.Id != item?.Id || latest?.TipVersionUrn != item?.TipVersionUrn)
                throw new InvalidDataException("Manifest changed during reconciliation.");

            if (item == null) await storage.CreateItemAsync(project, folder, ProjectManifest.FileName, bytes, ct);
            else await storage.PublishVersionAsync(project, folder, item.Id, bytes, ct);
            return new(DateTimeOffset.UtcNow, candidate.PublishedAtUtc, true, requests.Count);
        }
        finally { Gate.Release(); }
    }
}