namespace DigitalMIDP.Application.Manifests;

/// <summary>Compares the inventory visible to the operator with an optional previous project manifest.</summary>
public interface IProjectInventoryReconciler
{
    /// <summary>Scans without publishing. An incomplete read throws; an unchanged read returns the previous manifest.</summary>
    Task<InventoryReconciliation> ReconcileAsync(string hubId, string projectId, ProjectManifest? previous,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryReconciliation(bool Changed, ProjectManifest Manifest);
