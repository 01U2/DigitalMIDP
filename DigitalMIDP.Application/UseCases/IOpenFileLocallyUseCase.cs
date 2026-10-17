namespace DigitalMIDP.Application.UseCases;

using Dtos;

/// <summary>
/// Use case for opening a file locally via Desktop Connector, with fallback to web view.
/// </summary>
public interface IOpenFileLocallyUseCase
{
    /// <summary>
    /// Attempts to open a file locally, falling back to web view URL if unavailable.
    /// </summary>
    /// <remarks>
    /// Logic flow:
    /// 1. Check Desktop Connector installation
    /// 2. Validate project is synced
    /// 3. Resolve full local file path
    /// 4. Attempt to open file locally
    /// 5. Return result (success with local path, or failure with fallback URL)
    /// </remarks>
    /// <param name="item">The file item to open (must include pathInProject, webViewLink, etc.).</param>
    /// <param name="hubName">The hub name (e.g., 'YourHub').</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>FileOpenResult with success status and fallback URL if applicable.</returns>
    Task<FileOpenResult> ExecuteAsync(
        ItemDto item,
        string hubName,
        string projectName,
        CancellationToken cancellationToken);
}
