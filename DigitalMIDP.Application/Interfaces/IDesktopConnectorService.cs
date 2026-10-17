namespace DigitalMIDP.Application.Interfaces;

using Dtos;

/// <summary>
/// Service for interacting with Autodesk Desktop Connector.
/// Provides detection, project sync validation, local path resolution, and file opening capabilities.
/// </summary>
public interface IDesktopConnectorService
{
    /// <summary>
    /// Detects if Desktop Connector is installed and returns installation information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>DesktopConnectorInfo if installed, null otherwise.</returns>
    Task<DesktopConnectorInfo?> DetectInstallationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets information about a synced project in Desktop Connector.
    /// </summary>
    /// <param name="hubName">The hub name (e.g., 'YourHub').</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>SyncedProject if the project is synced, null otherwise.</returns>
    Task<SyncedProject?> GetSyncedProjectAsync(
        string hubName,
        string projectName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the full local file path for a file in a synced Desktop Connector project.
    /// </summary>
    /// <param name="hubName">The hub name.</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="relativePath">The relative path from the 'Project Files' folder.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Full local file path if the file exists and is synced, null otherwise.</returns>
    Task<string?> ResolveLocalFilePathAsync(
        string hubName,
        string projectName,
        string relativePath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to open a file locally via Desktop Connector, with optional fallback to web view URL.
    /// </summary>
    /// <param name="hubName">The hub name.</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="relativePath">The relative path from the 'Project Files' folder.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>FileOpenResult with success status and optional fallback URL.</returns>
    Task<FileOpenResult> OpenFileAsync(
        string hubName,
        string projectName,
        string relativePath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a file's local path via Desktop Connector and opens its containing folder in
    /// Windows Explorer with the file selected/highlighted.
    /// </summary>
    /// <param name="hubName">The hub name.</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="relativePath">The relative path from the 'Project Files' folder.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>FileOpenResult with success status; LocalPath holds the resolved file path.</returns>
    Task<FileOpenResult> OpenFileLocationAsync(
        string hubName,
        string projectName,
        string relativePath,
        CancellationToken cancellationToken);
}
