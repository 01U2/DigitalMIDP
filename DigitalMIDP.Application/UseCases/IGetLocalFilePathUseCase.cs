namespace DigitalMIDP.Application.UseCases;

using Dtos;

/// <summary>
/// Use case for resolving a file's local Desktop Connector path without opening anything - the
/// "Copy Path" context menu action's counterpart to <see cref="IOpenFileLocationUseCase"/>, which
/// resolves the same path but also opens Explorer with it selected.
/// </summary>
public interface IGetLocalFilePathUseCase
{
    /// <summary>
    /// Resolves the file's local path via Desktop Connector.
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
