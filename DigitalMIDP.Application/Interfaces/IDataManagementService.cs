using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.Interfaces;

/// <summary>
/// Service interface for Autodesk Platform Services (APS) Data Management API.
/// Provides access to hubs, projects, folders, and files.
/// </summary>
public interface IDataManagementService
{
    /// <summary>
    /// Drops everything this service has cached about one project: its custom attribute definitions,
    /// naming standard, top folders, root folder, and its files' paths.
    ///
    /// <para>
    /// This is what makes an explicit Refresh mean what the user expects. Caching project
    /// configuration is what removed most of the repeated APS traffic, but it also means a change
    /// made in ACC's web UI would not show up until the entry expired; calling this first makes the
    /// next read go back to APS for everything.
    /// </para>
    /// </summary>
    /// <param name="projectId">The project whose cached data should be discarded.</param>
    void InvalidateProjectCache(string projectId);

    /// <summary>
    /// Retrieves all hubs accessible to the authenticated user.
    /// </summary>
    /// <param name="accessToken">The OAuth access token for API authentication.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Collection of hub DTOs.</returns>
    Task<IEnumerable<HubDto>> GetHubsAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all projects within a specific hub.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="accessToken">The OAuth access token for API authentication.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Collection of project DTOs.</returns>
    Task<IEnumerable<ProjectDto>> GetProjectsAsync(string hubId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the root folder of a project.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="accessToken">The OAuth access token for API authentication.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Folder DTO representing the root folder.</returns>
    Task<FolderDto> GetRootFolderAsync(string hubId, string projectId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all items (folders and files) within a specific folder.
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="folderId">The ID of the folder.</param>
    /// <param name="accessToken">The OAuth access token for API authentication.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Collection of item DTOs (both folders and files).</returns>
    Task<IEnumerable<ItemDto>> GetFolderItemsAsync(string projectId, string folderId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves project-level and top-folder custom attribute definitions, excluding filename-code
    /// components. These related attributes can exist without being listed in the naming convention's
    /// metadata and are used by both Edit Attributes and the New Deliverable wizard.
    /// </summary>
    /// <param name="hubId">The ID of the hub (required for topFolders endpoint).</param>
    /// <param name="projectId">The ID of the project (Data Management API format with "b." prefix).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Project naming standard with metadata field definitions.</returns>
    Task<ProjectNamingStandardDto> GetProjectNamingStandardAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves the project's naming convention as the ordered list of filename components, with
    /// each component's allowed values.
    ///
    /// This is the counterpart to <see cref="GetProjectNamingStandardAsync"/>, which returns the
    /// attributes that are deliberately NOT part of the filename (it drives the grid's dynamic
    /// columns and filters filename components out). Composing a new document number needs exactly
    /// what that method discards, so the two are kept separate rather than one being made to serve
    /// both purposes.
    /// </summary>
    /// <param name="hubId">The ID of the hub (required to discover which naming standard the project uses).</param>
    /// <param name="projectId">The ID of the project (Data Management API format with "b." prefix).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The naming convention, or <see cref="NamingConventionDto.None"/> when the project has no
    /// naming standard assigned or its definition could not be parsed.
    /// </returns>
    Task<NamingConventionDto> GetNamingConventionAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch-retrieves custom attribute values for multiple items/files in a project.
    /// Queries the APS BIM360 docs API to get current custom attribute values.
    /// </summary>
    /// <param name="projectId">The ID of the project (BIM360 format, without "b." prefix).</param>
    /// <param name="itemUrns">Collection of item URNs to fetch attributes for.</param>
    /// <param name="includePathMetadata">
    /// Also populate <see cref="ItemCustomAttributesResultDto.PathInProject"/> and
    /// <see cref="ItemCustomAttributesResultDto.WebViewLink"/>.
    ///
    /// <para>
    /// Off by default, and worth understanding before switching on: the attribute values arrive in
    /// ONE batch request, whereas paths come from a per-item endpoint, so asking for them turns this
    /// method from a single call into one call plus one request per urn. That cost was previously
    /// paid unconditionally by callers that read neither field - see the remarks in the
    /// implementation for the full history.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Collection of item custom attribute results.</returns>
    Task<IEnumerable<ItemCustomAttributesResultDto>> GetItemCustomAttributesAsync(
        string projectId,
        IEnumerable<string> itemUrns,
        bool includePathMetadata = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retrieves path and web view link metadata for a single item.
    /// Queries GET /data/v1/projects/{projectId}/items/{itemUrn}?includePathInProject=true
    /// This endpoint returns the item's path within the project and its ACC web viewer URL.
    /// </summary>
    /// <param name="projectId">The ID of the project (Data Management API format with "b." prefix).</param>
    /// <param name="itemUrn">The item URN (ID) to fetch metadata for.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Item path metadata containing pathInProject and webViewLink.</returns>
    Task<ItemPathMetadataDto?> GetItemPathMetadataAsync(
        string projectId,
        string itemUrn,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates custom attribute values for a specific file version in ACC/BIM 360.
    /// POST /bim360/docs/v1/projects/{projectId}/versions/{versionUrn}/custom-attributes:batch-update
    /// </summary>
    /// <param name="projectId">The ID of the project (Data Management API format with "b." prefix, or BIM360 format).</param>
    /// <param name="versionUrn">The URN of the specific file version to update.</param>
    /// <param name="updates">The attribute id/value pairs to assign. A null value clears the attribute.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The attribute results as confirmed by ACC.</returns>
    Task<IReadOnlyList<CustomAttributeUpdateResultDto>> UpdateItemCustomAttributesAsync(
        string projectId,
        string versionUrn,
        IReadOnlyCollection<CustomAttributeUpdateRequestDto> updates,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a subfolder in a project.
    /// POST /data/v1/projects/{projectId}/folders
    /// </summary>
    /// <param name="projectId">The ID of the project (Data Management API format with "b." prefix).</param>
    /// <param name="parentFolderId">The folder to create inside.</param>
    /// <param name="folderName">The name for the new folder.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The created folder.</returns>
    Task<FolderDto> CreateFolderAsync(
        string projectId,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a new file by copying an existing item's content under a new name.
    ///
    /// ACC has no "copy" or "instantiate template" operation, so this downloads the source item's
    /// content and uploads it back as a new item. The new file therefore inherits the template's
    /// bytes and, by extension, its format - the caller is responsible for giving
    /// <paramref name="newFileName"/> the template's extension.
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="targetFolderId">The folder the new file is created in.</param>
    /// <param name="templateItemId">The item id of the template to copy.</param>
    /// <param name="newFileName">The new file's name, including extension.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The created item, including its tip version urn - which is required to write custom
    /// attributes onto it, since attributes are version-scoped.
    /// </returns>
    Task<ItemDto> CreateItemFromTemplateAsync(
        string projectId,
        string targetFolderId,
        string templateItemId,
        string newFileName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a new file from in-memory content.
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="targetFolderId">The folder the new file is created in.</param>
    /// <param name="fileName">The new file's name, including extension.</param>
    /// <param name="content">The file's content. May be empty, which creates a zero-byte file.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The created item, including its tip version urn.</returns>
    Task<ItemDto> CreateItemAsync(
        string projectId,
        string targetFolderId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Downloads the content of an item's tip (latest) version.
    /// </summary>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="itemId">The item to download.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The item's bytes.</returns>
    Task<byte[]> DownloadItemContentAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default
    );
}
