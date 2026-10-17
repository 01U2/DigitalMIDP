namespace DigitalMIDP.Application.UseCases;

using Dtos;

/// <summary>
/// Use case for revealing a file's local folder location via Desktop Connector.
/// </summary>
public interface IOpenFileLocationUseCase
{
    /// <summary>
    /// Resolves the file's local path and opens its containing folder in Windows Explorer with
    /// the file selected.
    /// </summary>
    /// <param name="item">The file item to locate (must include pathInProject).</param>
    /// <param name="hubName">The hub name (e.g., 'YourHub').</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>FileOpenResult with success status and the resolved local path.</returns>
    Task<FileOpenResult> ExecuteAsync(
        ItemDto item,
        string hubName,
        string projectName,
        CancellationToken cancellationToken);
}
