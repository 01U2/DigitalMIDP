using System.Collections.Concurrent;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of <see cref="ICdeStructureUseCase"/>.
/// </summary>
public sealed class CdeStructureUseCase : ICdeStructureUseCase
{
    /// <summary>
    /// Accepted names for the template repository. The specification calls it "templates", but the
    /// singular is at least as common in practice, so both are accepted rather than failing on a
    /// folder the user can plainly see.
    /// </summary>
    private static readonly string[] TemplateFolderNames =
        ["templates", "template", "projecttemplates", "projecttemplate"];

    /// <summary>
    /// How long a resolved structure stays usable. Resolving costs roughly seven API round trips
    /// (root folder, its children, the naming convention, and the WIP listing), and a single pass
    /// through the creation wizard asks for it several times, so repeating all of that per call is
    /// a noticeable delay for the user. The window is deliberately short: folders added in ACC's web
    /// UI while the app is open should still appear without a restart, and a folder this app creates
    /// itself invalidates the entry immediately via <see cref="Invalidate"/>.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CachedStructure> _structureCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Caches the naming convention itself, keyed the same way and for the same reason as
    /// <see cref="_structureCache"/>. Resolving it is not cheap - a topFolders fetch, the naming
    /// standard's own definition, and a custom-attribute-definitions call per top folder - and every
    /// consumer that used to call <c>IDataManagementService.GetNamingConventionAsync</c> directly now
    /// goes through <see cref="GetNamingConventionAsync"/> instead, so this dictionary is what turns
    /// three uncached fetches per wizard open into one.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedConvention> _conventionCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<CdeStructureUseCase> _logger;

    public CdeStructureUseCase(
        IDataManagementService dataManagementService,
        ILogger<CdeStructureUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(logger);

        _dataManagementService = dataManagementService;
        _logger = logger;
    }

    public void Invalidate(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var removedStructure = _structureCache.TryRemove(projectId, out _);
        var removedConvention = _conventionCache.TryRemove(projectId, out _);

        if (removedStructure || removedConvention)
        {
            _logger.LogDebug("Invalidated cached CDE structure/convention for project {ProjectId}", projectId);
        }
    }

    public async Task<NamingConventionDto> GetNamingConventionAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        if (_conventionCache.TryGetValue(projectId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("Using cached naming convention for project {ProjectId}", projectId);
            return cached.Convention;
        }

        var convention = await _dataManagementService.GetNamingConventionAsync(hubId, projectId, cancellationToken);
        _conventionCache[projectId] = new CachedConvention(convention, DateTimeOffset.UtcNow + CacheLifetime);

        return convention;
    }

    public async Task<CdeStructureDto> GetStructureAsync(
        string hubId,
        string projectId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        if (_structureCache.TryGetValue(projectId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("Using cached CDE structure for project {ProjectId}", projectId);
            return cached.Structure;
        }

        _logger.LogInformation("Resolving CDE structure for project {ProjectId}", projectId);

        try
        {
            var rootFolder = await _dataManagementService.GetRootFolderAsync(
                hubId, projectId, accessToken, cancellationToken);

            var rootChildren = await GetSubfoldersAsync(projectId, rootFolder.Id, accessToken, cancellationToken);

            var lifecycleFolders = new Dictionary<CdeLifecycleStage, FolderDto>();
            foreach (var (stage, acceptedNames) in FolderNameMatcher.LifecycleFolderNames)
            {
                var match = FolderNameMatcher.FindByName(rootChildren, acceptedNames);
                if (match is not null)
                {
                    lifecycleFolders[stage] = match;
                }
                else
                {
                    _logger.LogWarning(
                        "Lifecycle folder for stage {Stage} not found under '{RootFolder}' in project {ProjectId}",
                        stage, rootFolder.Name, projectId);
                }
            }

            // Templates live one level deeper than they used to: inside the hidden admin folder (e.g.
            // "00 ProjectFiles") nested under "Project Files", not as a direct child of "Project Files"
            // itself - see FolderNameMatcher.IsHiddenAdminFolder and FileListingUseCase, which skips
            // recursing into that folder so its contents never show as real files in the grid. This is
            // the one sanctioned reader of what's inside it.
            var adminFolder = FolderNameMatcher.FindByName(rootChildren, FolderNameMatcher.HiddenAdminFolderNames);

            FolderDto? templatesFolder = null;
            if (adminFolder is not null)
            {
                var adminChildren = await GetSubfoldersAsync(projectId, adminFolder.Id, accessToken, cancellationToken);
                templatesFolder = FolderNameMatcher.FindByName(adminChildren, TemplateFolderNames);
            }

            if (templatesFolder is null)
            {
                _logger.LogWarning(
                    "Template folder not found under '{AdminFolder}' in project {ProjectId} - deliverables cannot be created from a template",
                    adminFolder?.Name ?? FolderNameMatcher.HiddenAdminFolderNames[0], projectId);
            }

            var disciplines = await ResolveDisciplinesAsync(
                hubId, projectId, lifecycleFolders.GetValueOrDefault(CdeLifecycleStage.Wip),
                accessToken, cancellationToken);

            _logger.LogInformation(
                "Resolved CDE structure for project {ProjectId}: lifecycle folders=[{Stages}], templates={TemplatesFound}, disciplines={DisciplineCount} ({ExistingCount} existing)",
                projectId,
                string.Join(", ", lifecycleFolders.Keys),
                templatesFolder is not null,
                disciplines.Count,
                disciplines.Count(d => d.Exists));

            var structure = new CdeStructureDto(lifecycleFolders, templatesFolder, disciplines);
            _structureCache[projectId] = new CachedStructure(structure, DateTimeOffset.UtcNow + CacheLifetime);

            return structure;
        }
        catch (Exceptions.ApplicationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to resolve CDE structure for project {ProjectId}", projectId);
            throw new Exceptions.ApplicationException(
                $"Failed to resolve CDE folder structure for project {projectId}", ex);
        }
    }

    /// <summary>
    /// Builds the discipline list from the naming convention's role field, then matches each role
    /// against the folders that actually exist under WIP.
    ///
    /// The role field is the source of truth rather than the folder listing: a discipline that has
    /// no folder yet is still a valid destination (the folder gets created on demand), so deriving
    /// the list from existing folders alone would hide exactly the case this feature has to handle.
    /// </summary>
    private async Task<IReadOnlyList<DisciplineFolderDto>> ResolveDisciplinesAsync(
        string hubId,
        string projectId,
        FolderDto? wipFolder,
        string accessToken,
        CancellationToken cancellationToken)
    {
        // Routed through the cache rather than calling the data service directly - GetStructureAsync
        // itself is what InitializeAsync's own direct convention fetch used to duplicate.
        var convention = await GetNamingConventionAsync(hubId, projectId, cancellationToken);
        var disciplineField = convention.DisciplineField;

        if (disciplineField is null)
        {
            _logger.LogWarning(
                "Naming convention '{Convention}' has no role/discipline field - WIP destinations cannot be derived. Fields: [{Fields}]",
                convention.Name,
                string.Join(", ", convention.Fields.Select(f => f.Name)));
            return [];
        }

        if (!disciplineField.HasOptions)
        {
            _logger.LogWarning(
                "Role field '{Field}' has no allowed values configured in ACC - no disciplines can be offered",
                disciplineField.Name);
            return [];
        }

        var existingFolders = wipFolder is null
            ? []
            : await GetSubfoldersAsync(projectId, wipFolder.Id, accessToken, cancellationToken);

        return disciplineField.Options
            .Where(o => !string.IsNullOrWhiteSpace(o.Value))
            .Select(option =>
            {
                var code = option.Value!.Trim();
                var description = option.Description?.Trim() ?? string.Empty;
                var expectedName = DisciplineFolderDto.BuildFolderName(code, description);

                var folder = FolderNameMatcher.FindDisciplineFolder(existingFolders, code, description, expectedName);

                if (folder is null)
                {
                    _logger.LogDebug(
                        "Discipline '{Expected}' has no folder under WIP yet - it will be created on first use",
                        expectedName);
                }

                return new DisciplineFolderDto(code, description, expectedName, folder);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<FolderDto>> GetSubfoldersAsync(
        string projectId,
        string folderId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var items = await _dataManagementService.GetFolderItemsAsync(
            projectId, folderId, accessToken, cancellationToken);

        return items
            .Where(i => i.Type == ItemType.Folder)
            .Select(i => new FolderDto
            {
                Id = i.Id,
                Name = i.Name,
                ParentFolderId = folderId
            })
            .ToList();
    }

    /// <summary>A resolved structure and the moment it stops being trusted.</summary>
    private sealed record CachedStructure(CdeStructureDto Structure, DateTimeOffset ExpiresAt);

    /// <summary>A resolved naming convention and the moment it stops being trusted.</summary>
    private sealed record CachedConvention(NamingConventionDto Convention, DateTimeOffset ExpiresAt);
}
