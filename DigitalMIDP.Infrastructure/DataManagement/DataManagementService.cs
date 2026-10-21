using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Infrastructure.Caching;
using DigitalMIDP.Infrastructure.DataManagement.Dtos;
using DigitalMIDP.Infrastructure.Http;
using DigitalMIDP.Infrastructure.Mappings;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using ApplicationException = DigitalMIDP.Application.Exceptions.ApplicationException;

namespace DigitalMIDP.Infrastructure.DataManagement;

/// <summary>
/// Implementation of IDataManagementService for Autodesk Platform Services (APS).
/// Provides access to hubs, projects, folders, files, and custom metadata through the APS APIs.
/// </summary>
internal sealed partial class DataManagementService : IDataManagementService
{
    // API Base URLs
    private const string DataApiBaseUrl = "https://developer.api.autodesk.com/data/v1";
    private const string Bim360DocsApiBaseUrl = "https://developer.api.autodesk.com/bim360/docs/v1";
    private const string ProjectApiBaseUrl = "https://developer.api.autodesk.com/project/v1";
    private const string OssApiBaseUrl = "https://developer.api.autodesk.com/oss/v2";
    private const int DefaultPageLimit = 200;
    private const int MaxPages = 100;

    /// <summary>
    /// How long a project's schema stays usable: its custom attribute definitions, naming standard,
    /// top folders, and root folder.
    ///
    /// <para>
    /// These are configuration, changed by a project administrator in ACC's web UI, not by anything
    /// this application does - which is why they are cacheable at all. Before this, every file-grid
    /// load re-fetched the lot: topFolders, one naming-standard definition per standard, the
    /// project-level definitions, and one definitions call per top folder. That ran on project
    /// selection, again on every manual Refresh, and again on each five-minute auto-refresh tick,
    /// returning byte-identical data every time.
    /// </para>
    ///
    /// <para>
    /// Five minutes is chosen against the thing that actually changes it: an administrator adding an
    /// attribute in ACC and then looking for it here. One refresh cycle later it appears, and a
    /// manual Refresh clears the cache outright (see <see cref="InvalidateProjectCache"/>), so
    /// nobody has to wait or restart.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SchemaCacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a file's path-and-web-link metadata stays usable.
    ///
    /// <para>
    /// This is the most expensive thing the application reads, because the endpoint is per-item: a
    /// 28-file project cost 28 HTTP requests, and the same 28 were re-issued on every refresh and
    /// on every deliverable creation. A file's path only changes if somebody moves or renames it in
    /// ACC - rare, and not something this application does.
    /// </para>
    ///
    /// <para>
    /// Held longer than the schema because a miss costs so much more and the data is so much more
    /// stable. A file genuinely moved in ACC shows its old path for at most this long, and a manual
    /// Refresh drops it immediately.
    /// </para>
    /// </summary>
    private static readonly TimeSpan PathMetadataCacheLifetime = TimeSpan.FromMinutes(15);

    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApsMetadataCache _cache;
    private readonly ILogger<DataManagementService> _logger;

    public DataManagementService(
        HttpClient httpClient,
        IHttpClientFactory httpClientFactory,
        IApsMetadataCache cache,
        ILogger<DataManagementService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public void InvalidateProjectCache(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        _logger.LogInformation("Dropping cached APS metadata for project {ProjectId}", projectId);
        _cache.InvalidateProject(projectId);
    }

    /// <summary>
    /// Retrieves all hubs accessible to the authenticated user.
    /// GET /project/v1/hubs
    /// </summary>
    public Task<IEnumerable<HubDto>> GetHubsAsync(string accessToken, CancellationToken cancellationToken)
    {
        // The hub list was re-fetched (every page) on each sign-in, and the project list on every
        // hub selection - including flipping back to a hub already looked at. Neither changes within
        // a session in any way the user is waiting on. Not project-scoped (projectId null), so a
        // project Refresh leaves them alone; sign-out clears the whole cache, so a second user never
        // sees the first user's hubs.
        return _cache.GetOrAddAsync(
            null,
            "hubs",
            SchemaCacheLifetime,
            ct => FetchHubsAsync(ct),
            cancellationToken);
    }

    private async Task<IEnumerable<HubDto>> FetchHubsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching hubs from APS");

        try
        {
            var hubs = new List<HubDto>();
            var nextUrl = $"{ProjectApiBaseUrl}/hubs?page[limit]={DefaultPageLimit}&page[number]=0";
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pageNumber = 0;

            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!visited.Add(nextUrl))
                {
                    _logger.LogWarning("Detected pagination loop while fetching hubs at {Url}", nextUrl);
                    break;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);

                _logger.LogDebug("Request: GET {Url}", nextUrl);
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                var apsResponse = ApsResponseGuard.EnsureDeserialized(
                    JsonSerializer.Deserialize<ApsHubsResponse>(
                        responseContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
                    $"hubs page {pageNumber}");

                // A hub with no id cannot be used to list projects, so it is dropped rather than
                // offered in the hub picker as an entry that fails the moment it is chosen.
                var pageHubs = ApsResponseGuard.KeepValid(
                    apsResponse.Data ?? [],
                    h => !string.IsNullOrWhiteSpace(h.Id),
                    _logger,
                    "hubs",
                    $"hubs page {pageNumber}");

                hubs.AddRange(pageHubs.Select(h => h.ToApplicationDto()));

                _logger.LogDebug(
                    "Hubs page {PageNumber}: {Status}, {Count} usable hub(s)",
                    pageNumber, ApsResponseGuard.Describe(response, responseContent), pageHubs.Count);

                nextUrl = apsResponse.Links?.Next?.Href;

                if (pageNumber++ >= MaxPages && !string.IsNullOrWhiteSpace(nextUrl))
                {
                    _logger.LogWarning("Reached max pagination depth ({MaxPages}) while fetching hubs; stopping to prevent infinite loop.", MaxPages);
                    break;
                }
            }

            _logger.LogInformation("Successfully fetched {HubCount} hubs", hubs.Count);
            return hubs;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching hubs");
            throw new ApplicationException("Unable to fetch hubs from APS", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching hubs");
            throw new ApplicationException($"Unexpected error fetching hubs: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Retrieves all projects within a specific hub.
    /// GET /project/v1/hubs/{hubId}/projects
    /// </summary>
    public Task<IEnumerable<ProjectDto>> GetProjectsAsync(
        string hubId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);

        // See GetHubsAsync for why this is cached and how it is cleared.
        return _cache.GetOrAddAsync(
            null,
            $"projects:{hubId}",
            SchemaCacheLifetime,
            ct => FetchProjectsAsync(hubId, ct),
            cancellationToken);
    }

    private async Task<IEnumerable<ProjectDto>> FetchProjectsAsync(
        string hubId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching projects for hub {HubId}", hubId);

        try
        {
            var projects = new List<ProjectDto>();
            var nextUrl = $"{ProjectApiBaseUrl}/hubs/{hubId}/projects?page[limit]={DefaultPageLimit}&page[number]=0";
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pageNumber = 0;

            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!visited.Add(nextUrl))
                {
                    _logger.LogWarning("Detected pagination loop while fetching projects for hub {HubId} at {Url}", hubId, nextUrl);
                    break;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);

                _logger.LogDebug("Request: GET {Url}", nextUrl);
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                var apsResponse = ApsResponseGuard.EnsureDeserialized(
                    JsonSerializer.Deserialize<ApsProjectsResponse>(
                        responseContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
                    $"projects page {pageNumber} of hub {hubId}");

                // Every later call keys off the project id, so a project that arrived without one is
                // dropped - selecting it would fail on the first folder request.
                var pageProjects = ApsResponseGuard.KeepValid(
                    apsResponse.Data ?? [],
                    pr => !string.IsNullOrWhiteSpace(pr.Id),
                    _logger,
                    "projects",
                    $"hub {hubId} page {pageNumber}");

                projects.AddRange(pageProjects.Select(pr => pr.ToApplicationDto()));

                _logger.LogDebug(
                    "Projects page {PageNumber} for hub {HubId}: {Status}, {Count} usable project(s)",
                    pageNumber, hubId, ApsResponseGuard.Describe(response, responseContent), pageProjects.Count);

                nextUrl = apsResponse.Links?.Next?.Href;

                if (pageNumber++ >= MaxPages && !string.IsNullOrWhiteSpace(nextUrl))
                {
                    _logger.LogWarning("Reached max pagination depth ({MaxPages}) while fetching projects for hub {HubId}; stopping to prevent infinite loop.", MaxPages, hubId);
                    break;
                }
            }

            _logger.LogInformation("Successfully fetched {ProjectCount} projects for hub {HubId}", projects.Count, hubId);
            return projects;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching projects for hub {HubId}", hubId);
            throw new ApplicationException($"Unable to fetch projects for hub {hubId}", ex);
        }
    }

    /// <summary>
    /// Retrieves the root folder of a project.
    /// GET /project/v1/hubs/{hubId}/projects/{projectId}/topFolders
    /// </summary>
    public Task<FolderDto> GetRootFolderAsync(
        string hubId,
        string projectId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        // A project's "Project Files" folder is fixed for the life of the project, yet this was
        // re-resolved at the start of every file listing, every CDE-structure resolution, and every
        // deliverable creation - a guaranteed-identical round trip each time.
        return _cache.GetOrAddAsync(
            projectId,
            $"rootFolder:{hubId}:{projectId}",
            SchemaCacheLifetime,
            ct => FetchRootFolderAsync(hubId, projectId, ct),
            cancellationToken);
    }

    private async Task<FolderDto> FetchRootFolderAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching root folder for project {ProjectId} in hub {HubId}", projectId, hubId);

        try
        {
            var url = $"{ProjectApiBaseUrl}/hubs/{hubId}/projects/{projectId}/topFolders?projectFilesOnly=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            _logger.LogDebug("Request: GET {Url}", url);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var apsResponse = ApsResponseGuard.EnsureDeserialized(
                await response.Content.ReadAsAsync<ApsFolderContentsResponse>(cancellationToken),
                $"root folder of project {projectId}");

            // Validated rather than assumed: a topFolders response with a null "data" array used to
            // throw NullReferenceException here, which told nobody anything. A folder with no id is
            // equally unusable - it is what every subsequent folder listing is keyed on.
            var topFolders = (apsResponse.Data ?? []).ToList();

            var projectFilesFolder = topFolders.FirstOrDefault(f =>
                !string.IsNullOrWhiteSpace(f.Id)
                && f.Attributes?.DisplayName?.Equals("Project Files", StringComparison.OrdinalIgnoreCase) == true);

            if (projectFilesFolder == null)
            {
                _logger.LogWarning(
                    "No usable 'Project Files' folder in the topFolders response for project {ProjectId} ({FolderCount} folder(s) returned)",
                    projectId, topFolders.Count);
                throw new ResourceNotFoundException("Project Files folder", projectId);
            }

            var folder = new FolderDto
            {
                Id = projectFilesFolder.Id,
                // "name" and "displayName" are both present on a folder and APS has not always
                // populated both. The match above was made on displayName, so falling back to it
                // means the folder cannot end up with a null name after being found by it.
                Name = projectFilesFolder.Attributes.Name
                       ?? projectFilesFolder.Attributes.DisplayName
                       ?? "Project Files",
                ParentFolderId = projectFilesFolder.Attributes.ParentId
            };

            _logger.LogInformation("Successfully fetched root folder (ID: {FolderId})", folder.Id);
            return folder;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching root folder");
            throw new ApplicationException($"Unable to fetch root folder for project {projectId}", ex);
        }
    }

    /// <summary>
    /// Retrieves all items (folders and files) within a specific folder.
    /// GET /data/v1/projects/{projectId}/folders/{folderId}/contents
    /// </summary>
    public async Task<IEnumerable<ItemDto>> GetFolderItemsAsync(
        string projectId,
        string folderId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);
        _logger.LogInformation("Fetching items for folder {FolderId} in project {ProjectId}", folderId, projectId);

        try
        {
            var items = new List<ItemDto>();
            var nextUrl = $"{DataApiBaseUrl}/projects/{projectId}/folders/{folderId}/contents?page[limit]={DefaultPageLimit}&page[number]=0";
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pageNumber = 0;

            while (!string.IsNullOrWhiteSpace(nextUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!visited.Add(nextUrl))
                {
                    _logger.LogWarning("Detected pagination loop while fetching folder items for folder {FolderId} in project {ProjectId} at {Url}", folderId, projectId, nextUrl);
                    break;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);

                _logger.LogDebug("Request: GET {Url}", nextUrl);
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var apsResponse = ApsResponseGuard.EnsureDeserialized(
                    await response.Content.ReadAsAsync<ApsFolderContentsResponse>(cancellationToken),
                    $"contents of folder {folderId}");

                // An entry with no id or no display name is dropped. This is the validation that
                // matters most for not losing or corrupting data: such an entry would be mapped into
                // a grid row the user can see but cannot open, edit, or match to a file in ACC, and
                // - if it happened to be a folder - would be recursed into as the id "", returning
                // the wrong folder's contents.
                var pageItems = ApsResponseGuard.KeepValid(
                    apsResponse.Data ?? [],
                    i => !string.IsNullOrWhiteSpace(i.Id)
                         && !string.IsNullOrWhiteSpace(i.Attributes?.DisplayName ?? i.Attributes?.Name),
                    _logger,
                    "folder entries",
                    $"folder {folderId} page {pageNumber}");

                items.AddRange(pageItems.Select(item => item.ToApplicationDto()));

                nextUrl = apsResponse.Links?.Next?.Href;

                if (pageNumber++ >= MaxPages && !string.IsNullOrWhiteSpace(nextUrl))
                {
                    _logger.LogWarning("Reached max pagination depth ({MaxPages}) while fetching folder items for folder {FolderId} in project {ProjectId}; stopping to prevent infinite loop.", MaxPages, folderId, projectId);
                    break;
                }
            }

            _logger.LogInformation("Successfully fetched {ItemCount} items for folder {FolderId}", items.Count, folderId);
            return items;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching items for folder {FolderId}", folderId);
            throw new ApplicationException($"Unable to fetch items for folder {folderId}", ex);
        }
    }

    /// <summary>
    /// Retrieves the set of custom attributes ACC web's own "Edit Attributes" modal would show for a
    /// project: every custom attribute definition configured across the project's top-level folders,
    /// EXCLUDING the fields that compose the project's actual filename code, resolved dynamically
    /// from its assigned naming standard(s) (see <see cref="GetNamingCodeAttributeIdsAsync"/>).
    /// Custom attribute definitions are configured per top-level folder in ACC, and this app loads
    /// files from the whole project at once (not one top folder at a time), so we union definitions
    /// across every top folder rather than just the first one - otherwise an attribute scoped to a
    /// top folder other than "Project Files" would never surface here even though files under it
    /// carry real values for it.
    /// GET /bim360/docs/v1/projects/{projectId}/folders/{folderId}/custom-attribute-definitions
    /// </summary>
    public Task<ProjectNamingStandardDto> GetProjectNamingStandardAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        // Cached as a whole result, not just per underlying request: resolving this costs a
        // topFolders call, one naming-standard fetch per assigned standard, the project-level
        // definitions, and one definitions call per top folder - between five and a dozen round
        // trips for a schema an administrator changes a few times a year.
        //
        // Deliberately keyed separately from GetNamingConventionAsync below. The two paths consume
        // overlapping sources but answer opposite questions (which attributes are NOT part of the
        // filename, versus which ones ARE), and sharing one cached result between them is exactly
        // the kind of conflation CLAUDE.md warns against - the underlying per-endpoint caches are
        // where the shared work is actually reused.
        return _cache.GetOrAddAsync(
            projectId,
            $"namingStandardSchema:{hubId}:{projectId}",
            SchemaCacheLifetime,
            ct => FetchProjectNamingStandardAsync(hubId, projectId, ct),
            cancellationToken);
    }

    private async Task<ProjectNamingStandardDto> FetchProjectNamingStandardAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching custom attribute schema for project {ProjectId}", projectId);

        try
        {
            // Convert projectId from Data Management format (b.xxx) to BIM360 format (xxx)
            var bim360ProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectId[2..]
                : projectId;

            // Get topFolders - custom-attribute-definitions is folder-scoped, so we need every
            // top-level folder's id, not just the first one.
            var topFolders = await GetTopFoldersAsync(hubId, projectId, cancellationToken);
            var topFolderIds = topFolders
                .Select(f => f.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Discover which naming standard(s), if any, are actually assigned across the project's
            // top folders, and resolve which attribute ids compose each one's filename code - this is
            // what makes the exclusion work for ANY naming standard, not just the specific ISO 19650
            // pattern this app was first built against.
            var namingStandardIds = topFolders
                .SelectMany(f => f.Attributes?.Extension?.Data?.NamingStandardIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation(
                "Resolved {Count} naming standard id(s) from top folders for project {ProjectId}: [{Ids}]",
                namingStandardIds.Count,
                projectId,
                string.Join(", ", namingStandardIds));

            var namingCodeAttributeIds = await GetNamingCodeAttributeIdsAsync(bim360ProjectId, namingStandardIds, cancellationToken);

            _logger.LogInformation(
                "Resolved {Count} filename-code attribute id(s) to exclude for project {ProjectId}: [{Ids}]",
                namingCodeAttributeIds.Count,
                projectId,
                string.Join(", ", namingCodeAttributeIds));

            // Fetch every custom attribute definition configured across all top folders AND at the
            // project level (newer ACC projects can register attributes without tying them to any
            // folder), dedupe by attribute id, then drop the ones that compose the project's actual
            // filename code.
            // Was a sequential loop: project level, then one awaited call per top folder. These are
            // independent reads of the same read-only endpoint family, so they are fetched together
            // instead - the same collector GetNamingConventionAsync already used, which also means
            // both paths now share the per-endpoint cache entries underneath it rather than each
            // issuing its own copy of every request.
            var fieldsByAttributeId = await CollectCustomAttributeDefinitionsAsync(
                bim360ProjectId, topFolderIds, cancellationToken);

            var metadata = fieldsByAttributeId.Values
                .Where(f => !IsNamingCodeComponent(f, namingCodeAttributeIds))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var field in metadata)
            {
                _logger.LogDebug(
                    "Custom attribute field '{FieldName}': type={FieldType}, optionCount={OptionCount}",
                    field.Name,
                    field.Type,
                    field.Options.Count);
            }

            _logger.LogInformation("Successfully fetched {MetadataCount} custom attribute fields for project {ProjectId}", metadata.Count, projectId);

            return new ProjectNamingStandardDto(
                Id: string.Empty,
                Name: metadata.Count > 0 ? "Custom Attributes" : "No Standard",
                Metadata: metadata
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching custom attribute schema for project {ProjectId}", projectId);
            throw new ApplicationException($"Unable to fetch custom attribute schema for project {projectId}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching custom attribute schema for project {ProjectId}", projectId);
            throw new ApplicationException($"Unexpected error fetching custom attribute schema: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Retrieves the project's naming convention as an ordered list of filename components with
    /// their allowed values - the data needed to compose a NEW document number, as opposed to
    /// <see cref="GetProjectNamingStandardAsync"/>, which returns the attributes that are
    /// deliberately not part of the filename.
    /// GET /bim360/docs/v1/projects/{projectId}/naming-standards/{id}
    /// </summary>
    public Task<NamingConventionDto> GetNamingConventionAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        // CdeStructureUseCase already caches this for its own callers; caching here as well covers
        // the ones that hold IDataManagementService directly, and means the two caches cannot
        // disagree about what the project's convention is.
        return _cache.GetOrAddAsync(
            projectId,
            $"namingConvention:{hubId}:{projectId}",
            SchemaCacheLifetime,
            ct => FetchNamingConventionAsync(hubId, projectId, ct),
            cancellationToken);
    }

    private async Task<NamingConventionDto> FetchNamingConventionAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching naming convention for project {ProjectId}", projectId);

        try
        {
            var bim360ProjectId = ToBim360ProjectId(projectId);

            var topFolders = await GetTopFoldersAsync(hubId, projectId, cancellationToken);
            var namingStandardIds = topFolders
                .SelectMany(f => f.Attributes?.Extension?.Data?.NamingStandardIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (namingStandardIds.Count == 0)
            {
                _logger.LogWarning(
                    "Project {ProjectId} has no naming standard assigned to any top folder - deliverable creation cannot compose a document number",
                    projectId);
                return NamingConventionDto.None;
            }

            // A project can technically carry more than one standard across its top folders. Use the
            // first that actually parses with filename fields, and say so when others are ignored,
            // rather than silently merging patterns that may be incompatible with each other.
            NamingStandardDefinitionDto? definition = null;
            foreach (var standardId in namingStandardIds)
            {
                var candidate = await GetNamingStandardDefinitionAsync(bim360ProjectId, standardId, cancellationToken);
                if (candidate?.Fields is { Length: > 0 })
                {
                    definition = candidate;
                    break;
                }

                _logger.LogWarning(
                    "Naming standard {StandardId} could not be used as a convention (null or zero 'fields')",
                    standardId);
            }

            if (definition?.Fields is null or { Length: 0 })
            {
                _logger.LogWarning(
                    "None of the {Count} naming standard(s) on project {ProjectId} yielded filename fields",
                    namingStandardIds.Count,
                    projectId);
                return NamingConventionDto.None;
            }

            if (namingStandardIds.Count > 1)
            {
                _logger.LogWarning(
                    "Project {ProjectId} has {Count} naming standards; using '{Name}' and ignoring the rest",
                    projectId,
                    namingStandardIds.Count,
                    definition.Name);
            }

            // The naming-standards endpoint names and orders the fields but does not reliably carry
            // their allowed values, while custom-attribute-definitions carries values but no order.
            // Neither source alone is sufficient, so join them on attribute id.
            var definitionsByAttributeId = await CollectCustomAttributeDefinitionsAsync(
                bim360ProjectId,
                topFolders.Select(f => f.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!),
                cancellationToken);

            var fields = definition.Fields
                .Select((component, index) => BuildConventionField(component, index, definitionsByAttributeId))
                .Where(f => f is not null)
                .Select(f => f!)
                .ToList();

            // Built the same way as the filename fields (BuildMetadataField mirrors
            // BuildConventionField), rather than the bare dictionary lookup this used to be: a
            // metadata attribute's own naming-standard entry carries its real required/optional flag,
            // and discarding that in favour of custom-attribute-definitions' looser default is exactly
            // what let this app create files ACC itself considered non-conforming - see the fail-safe
            // note on BuildMetadataField.
            var metadata = (definition.Metadata ?? [])
                .Select(component => BuildMetadataField(component, definitionsByAttributeId))
                .Where(f => f is not null)
                .Select(f => f!)
                .ToList();

            _logger.LogInformation(
                "Naming convention '{Name}' for project {ProjectId}: delimiter='{Delimiter}', fields=[{Fields}], metadata={MetadataCount}",
                definition.Name,
                projectId,
                definition.ResolvedDelimiter,
                string.Join(", ", fields.Select(f =>
                    $"{f.Name}({f.Options.Count} options, length={f.LengthHint ?? "unconstrained"})")),
                metadata.Count);

            foreach (var field in fields.Where(f => !f.HasOptions))
            {
                _logger.LogWarning(
                    "Naming convention field '{Field}' (attributeId={AttributeId}) resolved with no allowed values - it will have to be entered as free text",
                    field.Name,
                    field.AttributeId);
            }

            return new NamingConventionDto(
                Id: definition.Id ?? string.Empty,
                Name: definition.Name ?? string.Empty,
                Delimiter: definition.ResolvedDelimiter,
                Fields: fields,
                Metadata: metadata);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching naming convention for project {ProjectId}", projectId);
            throw new ApplicationException($"Unable to fetch naming convention for project {projectId}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error fetching naming convention for project {ProjectId}", projectId);
            throw new ApplicationException($"Unexpected error fetching naming convention: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Merges a naming-standard component (which supplies name and pattern position) with the
    /// matching custom attribute definition (which supplies allowed values), preferring whichever
    /// source actually produced options. Returns null for a component with no usable attribute id -
    /// such an entry cannot be written back to ACC, so carrying it into the wizard would only
    /// produce a field the user fills in that then silently fails to save.
    /// </summary>
    private NamingConventionFieldDto? BuildConventionField(
        NamingStandardComponentDto component,
        int position,
        IReadOnlyDictionary<int, MetadataFieldDisplayDto> definitionsByAttributeId)
    {
        var resolvedId = component.ResolvedId;
        if (resolvedId is <= 0 or > int.MaxValue)
        {
            return null;
        }

        var attributeId = (int)resolvedId;
        var definition = definitionsByAttributeId.GetValueOrDefault(attributeId);

        // Options from the naming standard win when present: they are the only source observed to
        // carry descriptions, and descriptions are what discipline folder names are built from.
        var standardOptions = component.ResolveOptions();
        var options = standardOptions.Count > 0 ? standardOptions : definition?.Options ?? [];

        var name = component.Name ?? definition?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var optional = ResolveOptional(name, "field",
            ResolveOptionalFlag(component.Optional, component.Required),
            definition?.Optional);

        return new NamingConventionFieldDto(
            AttributeId: attributeId,
            Name: name,
            Type: component.Type ?? definition?.Type ?? string.Empty,
            Optional: optional,
            Position: position,
            Options: options,
            DefaultValue: component.DefaultValue ?? definition?.DefaultValue,
            MinLength: component.ResolveMinLength(),
            MaxLength: component.ResolveMaxLength());
    }

    /// <summary>
    /// Builds one non-filename metadata attribute (Status, Revision, Title1/2/3, Classification, and
    /// similar) for the creation wizard, mirroring <see cref="BuildConventionField"/> - including
    /// consulting the naming standard component's own required/optional flag, which the metadata list
    /// used to skip entirely in favour of a bare dictionary lookup into custom-attribute-definitions.
    /// That gap is exactly what let this app treat a project-required attribute as optional, silently
    /// let the user skip it, and still report the deliverable as created - while ACC itself rejected
    /// the result as non-conforming and routed it to the project's holding area, because ACC's own
    /// required/optional configuration was never actually consulted for this list.
    /// </summary>
    private MetadataFieldDisplayDto? BuildMetadataField(
        NamingStandardComponentDto component,
        IReadOnlyDictionary<int, MetadataFieldDisplayDto> definitionsByAttributeId)
    {
        var resolvedId = component.ResolvedId;
        if (resolvedId is <= 0 or > int.MaxValue)
        {
            return null;
        }

        var attributeId = (int)resolvedId;
        var definition = definitionsByAttributeId.GetValueOrDefault(attributeId);

        var standardOptions = component.ResolveOptions();
        var options = standardOptions.Count > 0 ? standardOptions : definition?.Options ?? [];

        var name = component.Name ?? definition?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var optional = ResolveOptional(name, "metadata attribute",
            ResolveOptionalFlag(component.Optional, component.Required),
            definition?.Optional);

        return new MetadataFieldDisplayDto(
            Name: name,
            Type: component.Type ?? definition?.Type ?? string.Empty,
            Description: definition?.Description,
            Optional: optional,
            Options: options,
            DefaultValue: component.DefaultValue ?? definition?.DefaultValue,
            AttributeId: attributeId);
    }

    /// <summary>
    /// Merges an "optional" flag with its inverse-shaped "required" fallback (different ACC schema
    /// endpoints have used both names for the same concept - see the DTOs' own doc comments). Null
    /// means the source said nothing usable either way.
    /// </summary>
    private static bool? ResolveOptionalFlag(bool? optional, bool? required) =>
        optional ?? (required.HasValue ? !required.Value : null);

    /// <summary>
    /// Picks between the naming standard's own flag and custom-attribute-definitions' flag (the
    /// standard wins when both are present, for the same reason its options win), failing safe to
    /// "required" - not "optional" - when neither source resolved anything.
    ///
    /// The fail-safe direction is deliberate and specific to this feature: silently treating a
    /// required attribute as optional lets the wizard report success on a deliverable ACC then
    /// rejects as non-conforming, after the file already exists - the exact failure this method
    /// exists to prevent. Treating an actually-optional attribute as required merely asks the user for
    /// one more value, which is corrected on the spot, before anything is created.
    /// </summary>
    private bool ResolveOptional(string fieldName, string fieldKind, bool? fromStandard, bool? fromDefinition)
    {
        var optional = fromStandard ?? fromDefinition;

        if (optional is null)
        {
            _logger.LogWarning(
                "Naming convention {Kind} '{Field}' has no required/optional flag from ACC in either source - treating as required",
                fieldKind, fieldName);
            return false;
        }

        return optional.Value;
    }

    /// <summary>
    /// Fetches every custom attribute definition visible to the project, keyed by attribute id.
    /// Both sources are consulted for the reason documented on
    /// <see cref="GetProjectLevelCustomAttributeDefinitionsAsync"/>: newer ACC projects can register
    /// attributes at project level without tying them to any folder.
    /// </summary>
    private async Task<Dictionary<int, MetadataFieldDisplayDto>> CollectCustomAttributeDefinitionsAsync(
        string bim360ProjectId,
        IEnumerable<string> topFolderIds,
        CancellationToken cancellationToken)
    {
        var definitionsByAttributeId = new Dictionary<int, MetadataFieldDisplayDto>();

        // Project-level plus one call per top folder - independent requests to the same read-only
        // endpoint family, so they are fetched concurrently rather than one at a time. Sequentially
        // this was the largest single contributor to how long the deliverable wizard took to open.
        var distinctFolderIds = topFolderIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var projectLevelTask = GetProjectLevelCustomAttributeDefinitionsAsync(bim360ProjectId, cancellationToken);
        var folderTasks = distinctFolderIds
            .Select(folderId => GetFolderCustomAttributeDefinitionsAsync(bim360ProjectId, folderId, cancellationToken))
            .ToList();

        await Task.WhenAll(folderTasks.Prepend(projectLevelTask));

        // Applied in a fixed order (project-level first, then folders) despite fetching concurrently,
        // so a later source's value for a shared attribute id still wins deterministically - matching
        // the sequential version's behaviour exactly, not just its outcome on the common case.
        foreach (var field in projectLevelTask.Result)
        {
            definitionsByAttributeId[field.AttributeId] = field;
        }

        foreach (var task in folderTasks)
        {
            foreach (var field in task.Result)
            {
                definitionsByAttributeId[field.AttributeId] = field;
            }
        }

        return definitionsByAttributeId;
    }

    /// <summary>
    /// Retrieves a project's top-level folders, including the naming standard ids attached to them.
    /// GET /project/v1/hubs/{hubId}/projects/{projectId}/topFolders
    /// </summary>
    private Task<IReadOnlyList<TopFolderDto>> GetTopFoldersAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken)
    {
        // Both schema paths need this, and each of them needs it before it can work out what else to
        // request - so caching it is what lets them run back to back without re-fetching, and what
        // keeps a wizard open plus a grid refresh down to one call instead of three.
        return _cache.GetOrAddAsync(
            projectId,
            $"topFolders:{hubId}:{projectId}",
            SchemaCacheLifetime,
            ct => FetchTopFoldersAsync(hubId, projectId, ct),
            cancellationToken);
    }

    private async Task<IReadOnlyList<TopFolderDto>> FetchTopFoldersAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var url = $"{ProjectApiBaseUrl}/hubs/{hubId}/projects/{projectId}/topFolders";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        _logger.LogDebug("Request: GET {Url}", url);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

        var parsed = ApsResponseGuard.EnsureDeserialized(
            JsonSerializer.Deserialize<TopFoldersResponseDto>(
                rawJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
            $"top folders of project {projectId}");

        // Previously the whole body was logged at Information - and a topFolders body names every
        // top-level folder plus the naming standards attached to them. The count is what diagnoses
        // this call; the body only ever mattered when parsing failed, which the warning below covers.
        IReadOnlyList<TopFolderDto> folders = parsed.Data ?? [];

        if (folders.Count == 0)
        {
            _logger.LogWarning(
                "topFolders returned no folders for project {ProjectId} ({Status}); no schema can be resolved. Body: {Body}",
                projectId,
                ApsResponseGuard.Describe(response, rawJson),
                ApsResponseGuard.TruncateForLog(rawJson));
        }
        else
        {
            _logger.LogDebug(
                "topFolders for project {ProjectId}: {Count} folder(s) ({Status})",
                projectId, folders.Count, ApsResponseGuard.Describe(response, rawJson));
        }

        return folders;
    }

    /// <summary>
    /// Converts a Data Management project id ("b.&lt;guid&gt;") to the bare guid the BIM 360 Docs
    /// API expects. The two APIs disagree on this format and nearly every docs endpoint in this
    /// class needs the conversion.
    /// </summary>
    private static string ToBim360ProjectId(string projectId) =>
        projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? projectId[2..] : projectId;

    /// <summary>
    /// Resolves the actual set of attribute ids that compose the filename code for the given naming
    /// standard(s), by fetching each standard's definition and reading its "fields" array (the
    /// naming-convention components, as distinct from "metadata" - attributes registered under the
    /// same standard that stay independently editable, e.g. Status/Revision/Classification/Title).
    /// This is what lets exclusion work for any naming standard's actual field names, instead of the
    /// one specific pattern this app originally hardcoded.
    /// GET /bim360/docs/v1/projects/{projectId}/naming-standards/{id}
    /// </summary>
    private async Task<HashSet<int>> GetNamingCodeAttributeIdsAsync(
        string bim360ProjectId,
        IReadOnlyList<string> namingStandardIds,
        CancellationToken cancellationToken)
    {
        var attributeIds = new HashSet<int>();

        foreach (var standardId in namingStandardIds)
        {
            var definition = await GetNamingStandardDefinitionAsync(bim360ProjectId, standardId, cancellationToken);
            if (definition is null)
            {
                _logger.LogWarning("Naming standard {StandardId} could not be fetched/parsed at all", standardId);
                continue;
            }

            if (definition.Fields is null or { Length: 0 })
            {
                _logger.LogWarning(
                    "Naming standard {StandardId} ('{Name}') parsed with 0 'fields' entries - nothing will be excluded for it",
                    standardId,
                    definition.Name);
                continue;
            }

            _logger.LogInformation(
                "Naming standard {StandardId} ('{Name}') fields: [{Fields}]",
                standardId,
                definition.Name,
                string.Join(", ", definition.Fields.Select(f => $"{f.Name}(id={f.ResolvedId})")));

            foreach (var field in definition.Fields)
            {
                var id = field.ResolvedId;
                if (id is > 0 and <= int.MaxValue)
                {
                    attributeIds.Add((int)id);
                }
            }
        }

        return attributeIds;
    }

    private Task<NamingStandardDefinitionDto?> GetNamingStandardDefinitionAsync(
        string bim360ProjectId,
        string standardId,
        CancellationToken cancellationToken)
    {
        // Both schema paths resolve the same standard: one to learn which attribute ids make up the
        // filename (so it can exclude them), the other to learn their order and allowed values (so
        // it can compose one). Caching it means the pair costs one fetch rather than two.
        return _cache.GetOrAddAsync(
            bim360ProjectId,
            $"namingStandardDefinition:{bim360ProjectId}:{standardId}",
            SchemaCacheLifetime,
            ct => FetchNamingStandardDefinitionAsync(bim360ProjectId, standardId, ct),
            cancellationToken);
    }

    private async Task<NamingStandardDefinitionDto?> FetchNamingStandardDefinitionAsync(
        string bim360ProjectId,
        string standardId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{Bim360DocsApiBaseUrl}/projects/{bim360ProjectId}/naming-standards/{standardId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            _logger.LogDebug("Request: GET {Url}", url);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The failing body IS the diagnostic here, so it is kept - truncated, and only on
                // this path. A successful body is not logged at all: it lists every allowed value of
                // every naming field in the project.
                _logger.LogWarning(
                    "Failed to fetch naming standard {StandardId}: {Status}. Body: {Body}",
                    standardId,
                    ApsResponseGuard.Describe(response, rawJson),
                    ApsResponseGuard.TruncateForLog(rawJson));
                return null;
            }

            var definition = JsonSerializer.Deserialize<NamingStandardDefinitionDto>(
                rawJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (definition is null)
            {
                _logger.LogWarning(
                    "Naming standard {StandardId} returned a body that could not be read as JSON ({Status}). Body: {Body}",
                    standardId,
                    ApsResponseGuard.Describe(response, rawJson),
                    ApsResponseGuard.TruncateForLog(rawJson));
                return null;
            }

            _logger.LogDebug(
                "Naming standard {StandardId} ('{Name}'): {FieldCount} filename field(s), {MetadataCount} metadata attribute(s)",
                standardId,
                definition.Name,
                definition.Fields?.Length ?? 0,
                definition.Metadata?.Length ?? 0);

            return definition;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch naming standard {StandardId}", standardId);
            return null;
        }
    }

    /// <summary>
    /// True if the given field is a filename-code component of the project's actual, dynamically
    /// resolved naming standard(s). If no naming standard could be resolved (none assigned, or the
    /// naming-standards endpoint failed), <paramref name="namingCodeAttributeIds"/> is empty and
    /// nothing is excluded - every custom attribute is shown rather than guessing.
    /// </summary>
    private static bool IsNamingCodeComponent(MetadataFieldDisplayDto field, HashSet<int> namingCodeAttributeIds)
    {
        return namingCodeAttributeIds.Contains(field.AttributeId);
    }

    /// <summary>
    /// Retrieves every custom attribute definition configured for a folder (typically a top-level
    /// folder), independent of any naming standard. This is one of the two sources ACC web's
    /// "Project Admin &gt; Custom Attributes" screen can write to (the other being project-level -
    /// see <see cref="GetProjectLevelCustomAttributeDefinitionsAsync"/>).
    /// GET /bim360/docs/v1/projects/{projectId}/folders/{folderId}/custom-attribute-definitions
    /// </summary>
    private Task<IReadOnlyList<MetadataFieldDisplayDto>> GetFolderCustomAttributeDefinitionsAsync(
        string bim360ProjectId,
        string folderId,
        CancellationToken cancellationToken)
    {
        var url = $"{Bim360DocsApiBaseUrl}/projects/{bim360ProjectId}/folders/{folderId}/custom-attribute-definitions";
        return GetCustomAttributeDefinitionsAsync(url, $"folder {folderId}", cancellationToken);
    }

    /// <summary>
    /// Retrieves every custom attribute definition configured at the PROJECT level (not tied to any
    /// specific top-level folder). Newer ACC projects can register custom attributes this way, so an
    /// attribute created recently may only show up here even if it never appears in any folder-scoped
    /// call.
    /// GET /bim360/docs/v1/projects/{projectId}/custom-attribute-definitions
    /// </summary>
    private Task<IReadOnlyList<MetadataFieldDisplayDto>> GetProjectLevelCustomAttributeDefinitionsAsync(
        string bim360ProjectId,
        CancellationToken cancellationToken)
    {
        var url = $"{Bim360DocsApiBaseUrl}/projects/{bim360ProjectId}/custom-attribute-definitions";
        return GetCustomAttributeDefinitionsAsync(url, "project level", cancellationToken);
    }

    private Task<IReadOnlyList<MetadataFieldDisplayDto>> GetCustomAttributeDefinitionsAsync(
        string url,
        string contextLabel,
        CancellationToken cancellationToken)
    {
        // Keyed on the URL, so the project-level call and each folder-scoped call cache separately -
        // and so the grid's schema path and the creation wizard's convention path share whichever of
        // them they have in common instead of each issuing its own set.
        //
        // The project id is parsed back out of the URL purely so InvalidateProjectCache can find
        // these entries; it is not used to build the request.
        return _cache.GetOrAddAsync(
            ExtractProjectIdFromDocsUrl(url),
            $"customAttributeDefinitions:{url}",
            SchemaCacheLifetime,
            ct => FetchCustomAttributeDefinitionsAsync(url, contextLabel, ct),
            cancellationToken);
    }

    /// <summary>
    /// Pulls the project id out of a BIM 360 Docs URL (".../projects/{id}/...") so a cache entry can
    /// be tagged with the project it belongs to. Returns null when the shape does not match, which
    /// only costs that entry its per-project invalidation.
    /// </summary>
    private static string? ExtractProjectIdFromDocsUrl(string url)
    {
        const string marker = "/projects/";
        var start = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = url.IndexOf('/', start);

        return end < 0 ? url[start..] : url[start..end];
    }

    private async Task<IReadOnlyList<MetadataFieldDisplayDto>> FetchCustomAttributeDefinitionsAsync(
        string url,
        string contextLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            _logger.LogDebug("Request: GET {Url}", url);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch custom attribute definitions ({Context}): {Status}. Body: {Body}",
                    contextLabel,
                    ApsResponseGuard.Describe(response, rawJson),
                    ApsResponseGuard.TruncateForLog(rawJson));
                return [];
            }

            using var document = JsonDocument.Parse(rawJson);
            var resultsElement = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement,
                JsonValueKind.Object when document.RootElement.TryGetProperty("results", out var results) => results,
                JsonValueKind.Object when document.RootElement.TryGetProperty("data", out var data) => data,
                JsonValueKind.Object when document.RootElement.TryGetProperty("definitions", out var defs) => defs,
                _ => default
            };

            if (resultsElement.ValueKind != JsonValueKind.Array)
            {
                // This endpoint family has returned several different envelope shapes over time (see
                // the switch above and CLAUDE.md), so a shape nobody recognises is the one case
                // where the body earns its place in the log - truncated, and at Warning.
                _logger.LogWarning(
                    "Custom attribute definitions response ({Context}) had no recognizable array (rootKind={RootKind}). Body: {Body}",
                    contextLabel,
                    document.RootElement.ValueKind,
                    ApsResponseGuard.TruncateForLog(rawJson));
                return [];
            }

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var definitions = resultsElement
                .EnumerateArray()
                .Select(element => JsonSerializer.Deserialize<CustomAttributeDefinitionDto>(element.GetRawText(), jsonOptions))
                .Where(dto => dto is not null)
                .Select(dto => dto!)
                .ToList();

            var mapped = MapCustomAttributeDefinitions(definitions);

            if (mapped.Count < definitions.Count)
            {
                _logger.LogWarning(
                    "{Dropped} of {Total} custom attribute definitions ({Context}) were dropped due to missing/unrecognized field names",
                    definitions.Count - mapped.Count,
                    definitions.Count,
                    contextLabel);
            }
            else
            {
                _logger.LogDebug(
                    "Custom attribute definitions ({Context}): {Count} definition(s)", contextLabel, mapped.Count);
            }

            return mapped;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Non-fatal: other sources may still provide the schema.
            _logger.LogWarning(ex, "Failed to fetch custom attribute definitions ({Context})", contextLabel);
            return [];
        }
    }

    private static IReadOnlyList<MetadataFieldDisplayDto> MapCustomAttributeDefinitions(
        IEnumerable<CustomAttributeDefinitionDto>? definitions)
    {
        return definitions?
            .Where(d => d.Id is > 0 and <= int.MaxValue && !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new MetadataFieldDisplayDto(
                Name: d.Name,
                Type: d.Type ?? string.Empty,
                Description: d.Description,
                // Additive only: reads a "required" key when "optional" itself is absent, same as the
                // creation wizard's path. The fallback stays "optional" when neither is present -
                // this list feeds the grid/Edit Attributes, a deliberately separate path from
                // GetNamingConventionAsync (see CLAUDE.md), and its default direction is not changed.
                Optional: ResolveOptionalFlag(d.Optional, d.Required) ?? true,
                Options: d.ArrayValues?
                    .Select(v => new MetadataOptionDisplayDto(v, null))
                    .ToList() ?? [],
                DefaultValue: d.DefaultValue,
                AttributeId: (int)d.Id
            ))
            .ToList()
            ?? [];
    }

    /// <summary>
    /// Batch-retrieves custom attribute values for multiple items/files.
    /// Handles pagination automatically by splitting large requests into batches.
    /// POST /bim360/docs/v1/projects/{projectId}/versions:batch-get
    /// </summary>
    public async Task<IEnumerable<ItemCustomAttributesResultDto>> GetItemCustomAttributesAsync(
        string projectId,
        IEnumerable<string> itemUrns,
        bool includePathMetadata = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(itemUrns);

        var itemUrnsList = itemUrns
            .Where(urn => !string.IsNullOrWhiteSpace(urn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (itemUrnsList.Count == 0)
        {
            return [];
        }

        _logger.LogInformation("Batch-fetching custom attributes for {ItemCount} items", itemUrnsList.Count);

        // THE expensive part of this method, and until now it ran unconditionally for the benefit of
        // nobody: it issues one HTTP request PER ITEM against the per-item path endpoint, and the two
        // fields it fills - PathInProject and WebViewLink - were read by neither caller.
        //
        //   * FilesViewModel resolves paths in its own separate pass (EnrichItemsWithPathMetadataAsync)
        //     and explicitly does not take them from this result, because the batch-get only returns a
        //     row for items that have attributes and would otherwise blank out the links of the rest.
        //   * DocumentNumberUseCase only wants the attribute values, and calls this twice per file
        //     created.
        //
        // So creating one deliverable in a 28-file discipline folder spent ~56 requests populating
        // two fields that were then discarded - the bulk of the 28-40s a single creation took.
        // Off by default, and opt-in for a caller that genuinely wants both in one call.
        var itemPathMetadata = includePathMetadata
            ? await GetItemPathMetadataAsync(projectId, itemUrnsList, cancellationToken)
            : EmptyPathMetadata;

        // Convert projectId from Data Management format to BIM360 format (once, for all batches)
        var bim360ProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? projectId[2..]
            : projectId;

        // Process URNs in batches to avoid exceeding API limits
        const int BatchSize = 500;
        var allResults = new List<ItemCustomAttributesResultDto>();

        for (int i = 0; i < itemUrnsList.Count; i += BatchSize)
        {
            var batch = itemUrnsList.Skip(i).Take(BatchSize).ToList();
            var batchResults = await GetBatchAsync(bim360ProjectId, batch, itemPathMetadata, cancellationToken);
            allResults.AddRange(batchResults);
        }

        _logger.LogInformation("Successfully batch-fetched custom attributes for {ItemCount} items", allResults.Count);
        return allResults;
    }

    private sealed record ItemPathMetadata(string? PathInProject, string? WebViewLink);

    /// <summary>
    /// Stand-in for the path lookup when a caller did not ask for it: every <c>TryGetValue</c>
    /// simply misses and the two path fields come back null.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ItemPathMetadata> EmptyPathMetadata =
        new Dictionary<string, ItemPathMetadata>(StringComparer.OrdinalIgnoreCase);

    private async Task<Dictionary<string, ItemPathMetadata>> GetItemPathMetadataAsync(
        string projectId,
        IReadOnlyCollection<string> itemUrns,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentDictionary<string, ItemPathMetadata>(StringComparer.OrdinalIgnoreCase);

        // Serve whatever is already cached without issuing anything, and only request the rest. This
        // is what turns "Fetching path metadata for 28 items" on every refresh into a request count
        // that drops to zero once the project has been looked at - a file's path in ACC does not
        // change unless somebody moves it.
        var missing = new List<string>(itemUrns.Count);

        foreach (var urn in itemUrns)
        {
            if (_cache.TryGet<ItemPathMetadata?>(PathMetadataCacheKey(projectId, urn), out var cached))
            {
                if (cached is not null)
                {
                    results[urn] = cached;
                }

                continue;
            }

            missing.Add(urn);
        }

        if (missing.Count == 0)
        {
            _logger.LogInformation(
                "Path metadata for all {ItemCount} item(s) served from cache - no requests issued", itemUrns.Count);
            return new Dictionary<string, ItemPathMetadata>(results, StringComparer.OrdinalIgnoreCase);
        }

        _logger.LogInformation(
            "Fetching path metadata for {MissingCount} of {ItemCount} item(s); {CachedCount} served from cache",
            missing.Count, itemUrns.Count, itemUrns.Count - missing.Count);

        const int MaxConcurrentRequests = 8;
        using var throttler = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);

        var tasks = missing.Select(async urn =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var metadata = await GetCachedItemPathMetadataAsync(projectId, urn, cancellationToken);
                if (metadata != null)
                {
                    results[urn] = metadata;
                }
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new Dictionary<string, ItemPathMetadata>(results, StringComparer.OrdinalIgnoreCase);
    }

    private static string PathMetadataCacheKey(string projectId, string itemUrn) =>
        $"itemPath:{projectId}:{itemUrn}";

    /// <summary>
    /// One item's path metadata, through the cache. Concurrent callers for the same item share a
    /// single request, which matters because the grid's enrichment pass and a deliverable creation
    /// can be in flight over the same project at the same time.
    /// </summary>
    private async Task<ItemPathMetadata?> GetCachedItemPathMetadataAsync(
        string projectId,
        string itemUrn,
        CancellationToken cancellationToken)
    {
        var key = PathMetadataCacheKey(projectId, itemUrn);

        var metadata = await _cache.GetOrAddAsync(
            projectId,
            key,
            PathMetadataCacheLifetime,
            ct => GetItemPathMetadataInternalAsync(projectId, itemUrn, ct),
            cancellationToken);

        if (metadata is null)
        {
            // A null means APS answered 200 with no item data - an odd response, not a stable fact
            // about the file. Caching it would show the file with no path and no web link for the
            // full fifteen minutes; evicting it means the next load simply asks again. The fetch is
            // still shared while in flight, so a fan-out over the same item does not multiply.
            _cache.Invalidate(key);
        }

        return metadata;
    }

    /// <summary>
    /// Retrieves path and web view link metadata for a single item.
    /// Public interface method that returns Application DTO.
    /// GET /data/v1/projects/{projectId}/items/{itemUrn}?includePathInProject=true
    /// </summary>
    public async Task<ItemPathMetadataDto?> GetItemPathMetadataAsync(
        string projectId,
        string itemUrn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemUrn);

        // This is the method FilesViewModel calls once per file on every load and every refresh, so
        // going through the cache here is what removes the repeated per-file requests outright.
        var internalMetadata = await GetCachedItemPathMetadataAsync(projectId, itemUrn, cancellationToken);

        if (internalMetadata == null)
        {
            return null;
        }

        return new ItemPathMetadataDto
        {
            ItemUrn = itemUrn,
            PathInProject = internalMetadata.PathInProject,
            WebViewLink = internalMetadata.WebViewLink
        };
    }

    private async Task<ItemPathMetadata?> GetItemPathMetadataInternalAsync(
        string projectId,
        string itemUrn,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{DataApiBaseUrl}/projects/{projectId}/items/{itemUrn}?includePathInProject=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            _logger.LogDebug("Request: GET {Url}", url);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var itemResponse = await response.Content.ReadAsAsync<ApsItemPathResponseDto>(cancellationToken);

            if (itemResponse?.Data is null)
            {
                // Returning null (rather than an empty record) matters: it is what keeps the caller's
                // existing item untouched, so a file whose path could not be read keeps whatever path
                // it already had instead of having it blanked out. Not cached either - the cache only
                // stores what the factory returns on success, and a null here would otherwise pin
                // "no path" onto the file for the whole cache lifetime.
                _logger.LogWarning(
                    "Path metadata response for item {ItemUrn} carried no item data ({Status})",
                    itemUrn, ApsResponseGuard.Describe(response, null));
                return null;
            }

            var pathInProject = ParsePathInProject(itemResponse.Data.Attributes);

            return new ItemPathMetadata(pathInProject, itemResponse.Data.Links?.WebView?.Href);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching item path metadata for urn {ItemUrn}", itemUrn);
            throw new ApplicationException($"Unable to fetch item path metadata for urn {itemUrn}: {ex.Message}", ex);
        }
    }

    private static string? ParsePathInProject(ApsItemPathResponseDto.ItemAttributes? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        var pathElement = attributes.PathInProject;
        if (pathElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (pathElement.ValueKind == JsonValueKind.String)
        {
            return pathElement.GetString();
        }

        if (pathElement.ValueKind == JsonValueKind.Array)
        {
            var segments = pathElement.EnumerateArray()
                .Select(segment => segment.GetString())
                .Where(segment => !string.IsNullOrWhiteSpace(segment));

            return string.Join("/", segments);
        }

        return null;
    }

    private async Task<List<ItemCustomAttributesResultDto>> GetBatchAsync(
        string bim360ProjectId,
        List<string> urns,
        IReadOnlyDictionary<string, ItemPathMetadata> itemPathMetadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchGetUrl = $"{Bim360DocsApiBaseUrl}/projects/{bim360ProjectId}/versions:batch-get";
            var requestBody = new { urns = urns.ToArray() };

            using var request = new HttpRequestMessage(HttpMethod.Post, batchGetUrl)
            {
                Content = JsonContent.Create(requestBody)
            };

            _logger.LogDebug("Request: POST {Url} with {ItemCount} item id(s)", batchGetUrl, urns.Count);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // The body is every attribute value of every requested file. Only its size is logged -
            // the per-item counts below are what actually diagnose this call.
            _logger.LogDebug("Batch-get response: {Status}", ApsResponseGuard.Describe(response, responseContent));

            var batchResponse = ApsResponseGuard.EnsureDeserialized(
                JsonSerializer.Deserialize<BatchGetVersionsResponseDto>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
                $"custom attributes of {urns.Count} item(s)");

            if (batchResponse.Errors is { Length: > 0 })
            {
                _logger.LogWarning(
                    "Batch-get custom attributes returned {ErrorCount} errors.",
                    batchResponse.Errors.Length);
                foreach (var error in batchResponse.Errors)
                {
                    _logger.LogWarning("Batch-get error for item {ItemUrn}: {ErrorCode} - {Message}", error.ItemUrn, error.ErrorCode, error.Message);
                }
            }

            // Create a lookup by request item ID (results and request are in same order)
            // A result with no itemUrn cannot be matched back to the file it describes, and
            // previously became a row keyed on "" - which then collided with every other such row
            // when the caller built its urn -> result dictionary, silently overwriting them. Dropping
            // them keeps the mapping honest, and the warning says how many were lost.
            var usableResults = ApsResponseGuard.KeepValid(
                batchResponse.Results ?? [],
                r => !string.IsNullOrWhiteSpace(r.ItemUrn),
                _logger,
                "custom attribute results",
                $"versions:batch-get of {urns.Count} item(s)");

            var results = usableResults
                .Select(r =>
                {
                    // Use the itemUrn from the response - it matches the request item ID
                    var itemUrn = r.ItemUrn!;

                    itemPathMetadata.TryGetValue(itemUrn, out var pathMetadata);

                    var customAttributes = r.CustomAttributes?
                        .Where(ca => !string.IsNullOrWhiteSpace(ca.Name))
                        .GroupBy(ca => ca.Name!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase)
                        .AsReadOnly() ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase).AsReadOnly();

                    return new ItemCustomAttributesResultDto(
                        Urn: itemUrn,  // Use the itemUrn from response (matches request/folder contents ID)
                        Name: r.Name ?? string.Empty,
                        Title: r.Title,
                        LastModifiedTime: r.LastModifiedTime,
                        LastModifiedUserName: r.LastModifiedUserName,
                        RevisionNumber: r.RevisionNumber,
                        ApprovalStatus: r.ApprovalStatus?.Value,
                        PathInProject: pathMetadata?.PathInProject,
                        WebViewLink: pathMetadata?.WebViewLink,
                        Attributes: customAttributes,
                        VersionUrn: r.Urn
                    );
                })
                .ToList();

            _logger.LogDebug(
                "Batch-get mapped {ResultCount} result(s) carrying {AttributeCount} attribute value(s) in total",
                results.Count,
                results.Sum(r => r.Attributes.Count));

            return results;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error batch-fetching custom attributes for {ItemCount} items", urns.Count);
            throw new ApplicationException($"Unable to fetch custom attributes: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error batch-fetching custom attributes");
            throw new ApplicationException($"Unexpected error fetching custom attributes: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Updates custom attribute values for a specific file version.
    /// POST /bim360/docs/v1/projects/{projectId}/versions/{versionUrn}/custom-attributes:batch-update
    /// </summary>
    public async Task<IReadOnlyList<CustomAttributeUpdateResultDto>> UpdateItemCustomAttributesAsync(
        string projectId,
        string versionUrn,
        IReadOnlyCollection<CustomAttributeUpdateRequestDto> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionUrn);
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return [];
        }

        _logger.LogInformation(
            "Updating {AttributeCount} custom attributes for version {VersionUrn} in project {ProjectId}",
            updates.Count,
            versionUrn,
            projectId);

        // Convert projectId from Data Management format (b.xxx) to BIM360 format (xxx)
        var bim360ProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? projectId[2..]
            : projectId;

        // Version URNs typically look like "urn:adsk.wipprod:fs.file:vf.XXXX?version=3" - the "?" and "="
        // must be percent-encoded or they get parsed as a query string instead of part of the path.
        var encodedVersionUrn = Uri.EscapeDataString(versionUrn);
        var url = $"{Bim360DocsApiBaseUrl}/projects/{bim360ProjectId}/versions/{encodedVersionUrn}/custom-attributes:batch-update";
        var requestBody = updates
            .Select(u => new CustomAttributesBatchUpdateRequestItemDto { Id = u.AttributeId, Value = u.Value })
            .ToArray();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(requestBody)
            };

            _logger.LogDebug("Request: POST {Url} with {AttributeCount} attribute updates", url, updates.Count);
            var response = await _httpClient.SendAsync(request, cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Truncated: an ACC rejection names the offending attribute near the start of the
                // body, which is the useful part. The full body is still passed to
                // BuildAttributeValidationException below, because that becomes the message the user
                // reads - it just does not also go into the log.
                _logger.LogWarning(
                    "Custom attribute update failed for version {VersionUrn}: {Status}. Body: {Body}",
                    versionUrn,
                    ApsResponseGuard.Describe(response, responseContent),
                    ApsResponseGuard.TruncateForLog(responseContent));

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    throw new Application.Exceptions.PermissionDeniedException(
                        "You don't have permission to edit custom attributes on this file.");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    throw BuildAttributeValidationException(responseContent);
                }

                throw new ApplicationException(
                    $"Unable to update custom attributes (HTTP {(int)response.StatusCode}): {responseContent}");
            }

            var updateResponse = ApsResponseGuard.EnsureDeserialized(
                JsonSerializer.Deserialize<CustomAttributesBatchUpdateResponseDto>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
                $"custom attribute update of version {versionUrn}");

            var results = (updateResponse.Results ?? [])
                .Select(r => new CustomAttributeUpdateResultDto(r.Id, r.Name, r.Type, r.Value))
                .ToList();

            // A 2xx that confirms fewer attributes than were sent is the shape a silent partial save
            // takes: the call "succeeded", so nothing was retried, yet a value the user typed never
            // landed. It is not treated as a failure (the endpoint is all-or-nothing on validation,
            // so this is unexpected rather than routine) but it is no longer invisible.
            if (results.Count < updates.Count)
            {
                _logger.LogWarning(
                    "ACC accepted the update for version {VersionUrn} but confirmed only {ConfirmedCount} of {SentCount} attribute(s)",
                    versionUrn, results.Count, updates.Count);
            }
            else
            {
                _logger.LogInformation(
                    "Successfully updated {AttributeCount} custom attributes for version {VersionUrn}",
                    results.Count,
                    versionUrn);
            }

            return results;
        }
        catch (ApplicationException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error updating custom attributes for version {VersionUrn}", versionUrn);
            throw new ApplicationException($"Unable to reach Autodesk Forma: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating custom attributes for version {VersionUrn}", versionUrn);
            throw new ApplicationException($"Unexpected error updating custom attributes: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses ACC's structured 400 error body (e.g. code "CUSTOM_ATTRIBUTE_CONSTRAINT_FAIL") into a
    /// validation exception carrying the specific attribute id(s) that were rejected, so callers can
    /// map them back to field names for a user-friendly message. Falls back to the raw body if the
    /// response isn't in the expected shape.
    /// </summary>
    private static AttributeValidationException BuildAttributeValidationException(string responseContent)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<CustomAttributesUpdateErrorResponseDto>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (errorResponse is not null && (errorResponse.Detail is not null || errorResponse.Errors is { Length: > 0 }))
            {
                var errors = (errorResponse.Errors ?? [])
                    .Select(e => new Application.Exceptions.AttributeValidationError(e.Id, e.Value, errorResponse.Detail))
                    .ToList();

                var message = errorResponse.Detail ?? errorResponse.Title ?? "ACC rejected the attribute values.";
                return new AttributeValidationException(message, errors);
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw-body fallback below.
        }

        return new AttributeValidationException($"ACC rejected the attribute values: {responseContent}");
    }
}
