namespace DigitalMIDP.Application.Manifests;

public sealed record LocalManifestProject(string HubId, string ProjectId, string HubName, string ProjectName);

public enum LocalManifestStatus { Available, Stale, Unavailable }

public sealed record LocalManifestSnapshot(LocalManifestStatus Status, ProjectManifest? Manifest, string? Reason);

/// <summary>Reads the locally synced project catalog without querying APS or requesting hydration.</summary>
public interface ILocalManifestReader
{
    /// <summary>Returns a validated snapshot, a stale last-good snapshot, or unavailable when neither can be read.</summary>
    Task<LocalManifestSnapshot> ReadAsync(LocalManifestProject project, CancellationToken ct = default);

    /// <summary>Yields the initial snapshot, then changes. Cancel or dispose the enumeration to stop watching.</summary>
    IAsyncEnumerable<LocalManifestSnapshot> WatchAsync(LocalManifestProject project, CancellationToken ct = default);
}
