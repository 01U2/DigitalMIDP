namespace DigitalMIDP.Application.Manifests;

/// <summary>Schedules checks for the signed-in operator's persisted project enrollments.</summary>
public interface IManifestRunner
{
    /// <summary>Signals that the operator should reread the status snapshot.</summary>
    event EventHandler? StatusChanged;
    /// <summary>Returns current scheduling state and last check results.</summary>
    IReadOnlyList<ManifestProjectStatus> GetSnapshot();
    /// <summary>Loads enrollments and starts scheduling checks.</summary>
    Task StartAsync();
    /// <summary>Stops checks without removing persisted enrollments.</summary>
    Task StopAsync();
    /// <summary>Enrolls a project or changes its interval; a null interval uses ten minutes.</summary>
    Task EnableAsync(string hubId, string projectId, TimeSpan? interval = null);
    /// <summary>Removes the enrollment without deleting its published manifest.</summary>
    Task DisableAsync(string hubId, string projectId);
    /// <summary>Starts a check or awaits the enrolled project's active check.</summary>
    Task<ManifestCheckResult> CheckNowAsync(string hubId, string projectId, CancellationToken ct = default);
}

public sealed record ManifestProjectStatus(string HubId, string ProjectId, TimeSpan Interval,
    bool IsChecking, DateTimeOffset? NextCheckUtc, ManifestCheckResult? LastSuccess,
    DateTimeOffset? LastAttemptUtc, string? LastError);
