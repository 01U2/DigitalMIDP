using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Use case interface for handling hub and project selection and file listing.
/// </summary>
public interface IFileListingUseCase
{
    /// <summary>
    /// Retrieves all hubs accessible to the user.
    /// </summary>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Response containing available hubs.</returns>
    Task<GetHubsResponseDto> GetHubsAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all projects within a selected hub.
    /// </summary>
    /// <param name="hubId">The ID of the selected hub.</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Response containing projects in the hub.</returns>
    Task<GetProjectsResponseDto> GetProjectsAsync(string hubId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves folder items (folders and files) for a given folder in a project.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="folderId">The ID of the folder (or root if null).</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Response containing items in the folder.</returns>
    Task<GetFolderItemsResponseDto> GetFolderItemsAsync(string hubId, string projectId, string? folderId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all files within a project, including nested folders.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Response containing all files in the project.</returns>
    Task<GetFolderItemsResponseDto> GetAllProjectFilesAsync(string hubId, string projectId, string accessToken, CancellationToken cancellationToken);
}
