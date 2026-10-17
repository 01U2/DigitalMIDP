using DigitalMIDP.Domain.Entities;
using DigitalMIDP.Domain.ValueObjects;

namespace DigitalMIDP.Domain.Interfaces;

/// <summary>
/// Repository interface for APS Data Management operations.
/// Implemented by Infrastructure layer.
/// </summary>
public interface IDataManagementRepository
{
    /// <summary>
    /// Lists all hubs accessible to the authenticated user.
    /// </summary>
    Task<IReadOnlyList<Hub>> GetHubsAsync(
        AccessToken accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all projects within a hub.
    /// </summary>
    Task<IReadOnlyList<Project>> GetProjectsAsync(
        string hubId,
        AccessToken accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the top-level folders for a project.
    /// </summary>
    Task<IReadOnlyList<Folder>> GetTopFoldersAsync(
        string projectId,
        AccessToken accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the contents (folders and files) of a specific folder.
    /// </summary>
    Task<FolderContents> GetFolderContentsAsync(
        string projectId,
        string folderId,
        AccessToken accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recursively lists all files under a folder.
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetFilesRecursivelyAsync(
        string projectId,
        string folderId,
        AccessToken accessToken,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the contents of a folder (subfolders and files).
/// </summary>
public sealed record FolderContents
{
    public required IReadOnlyList<Folder> Folders { get; init; }
    public required IReadOnlyList<FileItem> Files { get; init; }
}
