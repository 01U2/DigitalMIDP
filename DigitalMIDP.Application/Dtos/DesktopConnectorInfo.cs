namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Information about the detected Desktop Connector installation.
/// </summary>
public record DesktopConnectorInfo(
    bool IsInstalled,
    string? Version,
    string? BaseInstallPath,
    string? SyncRootPath);
