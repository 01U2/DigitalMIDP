namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Information about a Desktop Connector synced project.
/// </summary>
public record SyncedProject(
    string HubName,
    string ProjectName,
    string LocalPath,
    DateTime? LastSyncTime,
    SyncStatus Status);
