namespace DigitalMIDP.Application.Manifests;

/// <summary>Checks one project's APS inventory and publishes the manifest only when the inventory changes.</summary>
public interface IManifestPublisher
{
    /// <summary>Seeds the manifest if absent, otherwise reconciles against the validated tip.</summary>
    Task<ManifestCheckResult> CheckAsync(string hubId, string projectId, CancellationToken ct = default);
}

public sealed record ManifestCheckResult(DateTimeOffset CheckedAtUtc, DateTimeOffset LastPublicationUtc,
    bool Changed, int HttpRequestCount);