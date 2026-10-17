using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of IFileListingUseCase.
/// Orchestrates hub, project, and file listing operations.
/// </summary>
public sealed class FileListingUseCase : IFileListingUseCase
{
    /// <summary>
    /// How deep the recursive folder walk will go.
    ///
    /// <para>
    /// A real ACC project is nowhere near this deep - the CDE structure is three or four levels -
    /// so this is not a limit on legitimate projects but a stop on a walk that is not terminating.
    /// The walk follows folder ids returned by APS, and if a listing ever returns a folder that
    /// contains itself (directly, or through its own ancestor), the recursion has no natural end:
    /// it would recurse until the stack overflowed, taking the whole application with it. The
    /// visited-id set below catches the common shapes; this catches whatever it does not.
    /// </para>
    /// </summary>
    private const int MaxFolderDepth = 32;

    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<FileListingUseCase> _logger;

    public FileListingUseCase(
        IDataManagementService dataManagementService,
        ILogger<FileListingUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(logger);

        _dataManagementService = dataManagementService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all hubs accessible to the user.
    /// </summary>
    public async Task<GetHubsResponseDto> GetHubsAsync(string accessToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        try
        {
            var hubs = await _dataManagementService.GetHubsAsync(accessToken, cancellationToken);

            return new GetHubsResponseDto
            {
                Hubs = hubs
            };
        }
        catch (Exception ex)
        {
            throw new Exceptions.ApplicationException("Failed to retrieve hubs", ex);
        }
    }

    /// <summary>
    /// Retrieves all projects within a hub.
    /// </summary>
    public async Task<GetProjectsResponseDto> GetProjectsAsync(
        string hubId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        try
        {
            var projects = await _dataManagementService.GetProjectsAsync(hubId, accessToken, cancellationToken);

            return new GetProjectsResponseDto
            {
                Projects = projects
            };
        }
        catch (Exception ex)
        {
            throw new Exceptions.ApplicationException($"Failed to retrieve projects for hub {hubId}", ex);
        }
    }

    /// <summary>
    /// Retrieves items (folders and files) within a folder.
    /// </summary>
    public async Task<GetFolderItemsResponseDto> GetFolderItemsAsync(
        string hubId,
        string projectId,
        string? folderId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        try
        {
            // If folderId is null, get root folder first
            string folderIdToUse = folderId ?? "root";

            if (folderId == null)
            {
                var rootFolder = await _dataManagementService.GetRootFolderAsync(hubId, projectId, accessToken, cancellationToken);
                folderIdToUse = rootFolder.Id;
            }

            var items = await _dataManagementService.GetFolderItemsAsync(projectId, folderIdToUse, accessToken, cancellationToken);

            return new GetFolderItemsResponseDto
            {
                Items = items
            };
        }
        catch (Exception ex)
        {
            throw new Exceptions.ApplicationException($"Failed to retrieve folder items for project {projectId}", ex);
        }
    }

    /// <summary>
    /// Retrieves all files within a project, including nested folders.
    /// </summary>
    public async Task<GetFolderItemsResponseDto> GetAllProjectFilesAsync(
        string hubId,
        string projectId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var rootFolder = await _dataManagementService.GetRootFolderAsync(hubId, projectId, accessToken, cancellationToken);

        if (string.IsNullOrWhiteSpace(rootFolder?.Id))
        {
            // Everything below is keyed on this id. Walking from a blank one would list the wrong
            // folder (or nothing) and report an empty project as though that were the truth, which
            // is the worst possible outcome for a files register - it looks like data loss.
            throw new Exceptions.ApplicationException(
                $"APS returned no usable root folder for project {projectId}, so its files cannot be listed.");
        }

        var results = new List<ItemDto>();

        // Guards against a folder graph that is not a tree. Folder listings come from APS and are
        // not validated to be acyclic; a folder reachable from its own descendant would otherwise be
        // walked forever, and - more insidiously - files under a folder reachable by two paths would
        // appear twice in the grid, each copy with a different Location.
        var visitedFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await CollectProjectFilesAsync(
            projectId,
            rootFolder.Id,
            rootFolder.Name ?? string.Empty,
            accessToken,
            results,
            visitedFolderIds,
            depth: 0,
            cancellationToken);

        _logger.LogInformation(
            "Collected {FileCount} file(s) from {FolderCount} folder(s) in project {ProjectId}",
            results.Count, visitedFolderIds.Count, projectId);

        return new GetFolderItemsResponseDto
        {
            Items = results
        };
    }

    private async Task CollectProjectFilesAsync(
        string projectId,
        string folderId,
        string currentPath,
        string accessToken,
        List<ItemDto> results,
        HashSet<string> visitedFolderIds,
        int depth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!visitedFolderIds.Add(folderId))
        {
            // Already walked. Returning rather than throwing: a duplicate reference is not a reason
            // to fail the whole listing, and everything under this folder has already been collected
            // once, under the path it was first reached by.
            _logger.LogWarning(
                "Folder {FolderId} was reached twice while listing project {ProjectId} (second path: '{Path}') - not walking it again",
                folderId, projectId, currentPath);
            return;
        }

        if (depth >= MaxFolderDepth)
        {
            _logger.LogWarning(
                "Stopped at depth {Depth} under '{Path}' in project {ProjectId}; any folders below this are not listed",
                depth, currentPath, projectId);
            return;
        }

        var items = await _dataManagementService.GetFolderItemsAsync(projectId, folderId, accessToken, cancellationToken);

        if (items is null)
        {
            _logger.LogWarning(
                "Folder {FolderId} in project {ProjectId} returned no item collection at all", folderId, projectId);
            return;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Validated before use, not after. An entry with no id cannot be fetched, opened, or
            // have its attributes written; an entry with no name cannot be matched to a file in ACC
            // or parsed for its document number. Carrying either into the grid produces a row that
            // looks real and fails on every interaction - and for a folder, recursing on a blank id
            // would list some other folder entirely and file its contents under the wrong path.
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
            {
                _logger.LogWarning(
                    "Skipping an entry under '{Path}' in project {ProjectId} that arrived without an id or a name (id='{Id}', name='{Name}')",
                    currentPath, projectId, item?.Id, item?.Name);
                continue;
            }

            if (item.Type == ItemType.Folder)
            {
                // Never walk into the hidden admin/reference folder (e.g. "00 ProjectFiles") - its
                // contents (file-creation templates and similar) are not real project deliverables and
                // must never appear as rows in the files grid, regardless of the user's own ACC folder
                // permissions there. See FolderNameMatcher.IsHiddenAdminFolder.
                if (FolderNameMatcher.IsHiddenAdminFolder(item.Name))
                {
                    continue;
                }

                var nextPath = string.IsNullOrWhiteSpace(currentPath)
                    ? item.Name
                    : $"{currentPath}/{item.Name}";

                await CollectProjectFilesAsync(
                    projectId, item.Id, nextPath, accessToken, results, visitedFolderIds, depth + 1, cancellationToken);
                continue;
            }

            // A file with no urn cannot have its custom attributes read or written - the attributes
            // endpoints are keyed on it. It is still listed, because the file genuinely exists and
            // hiding it would be the data loss this validation is meant to prevent; the log says why
            // its attribute columns will be empty.
            if (string.IsNullOrWhiteSpace(item.ItemUrn))
            {
                _logger.LogWarning(
                    "File '{FileName}' under '{Path}' has no item urn; its custom attributes cannot be read or edited",
                    item.Name, currentPath);
            }

            results.Add(new ItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Type = item.Type,
                // Autodesk's item API doesn't actually return attributes.parentId (that's not a
                // real field in the response), so item.ParentId is always null. We already know
                // the true containing folder id here - it's the folder we just walked to find
                // this item - so use that directly instead.
                ParentId = folderId,
                ItemUrn = item.ItemUrn,
                Location = currentPath,
                Size = item.Size,
                CreatedAt = item.CreatedAt,
                ModifiedAt = item.ModifiedAt,
                // Derived from the path already walked to reach this file - no extra API call.
                LifecycleStage = FolderNameMatcher.ClassifyLifecycleStage(currentPath)
            });
        }
    }
}
