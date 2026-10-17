using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of <see cref="ITemplateCatalogUseCase"/>.
/// </summary>
public sealed class TemplateCatalogUseCase : ITemplateCatalogUseCase
{
    private readonly ICdeStructureUseCase _cdeStructureUseCase;
    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<TemplateCatalogUseCase> _logger;

    public TemplateCatalogUseCase(
        ICdeStructureUseCase cdeStructureUseCase,
        IDataManagementService dataManagementService,
        ILogger<TemplateCatalogUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(cdeStructureUseCase);
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(logger);

        _cdeStructureUseCase = cdeStructureUseCase;
        _dataManagementService = dataManagementService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeliverableTemplateDto>> GetTemplatesAsync(
        string hubId,
        string projectId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        _logger.LogInformation("Listing templates for project {ProjectId}", projectId);

        var structure = await _cdeStructureUseCase.GetStructureAsync(
            hubId, projectId, accessToken, cancellationToken);

        if (structure.TemplatesFolder is null)
        {
            _logger.LogWarning("Project {ProjectId} has no template folder - returning no templates", projectId);
            return [];
        }

        return await GetTemplatesInFolderAsync(
            projectId, structure.TemplatesFolder, accessToken, cancellationToken);
    }

    /// <summary>
    /// Lists the files directly inside the template folder. Subfolders are not descended into: the
    /// specification places templates directly in the repository, and a nested folder is more likely
    /// to be organisational (or an unrelated stray) than a template the user meant to offer.
    /// </summary>
    private async Task<IReadOnlyList<DeliverableTemplateDto>> GetTemplatesInFolderAsync(
        string projectId,
        FolderDto templatesFolder,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var items = await _dataManagementService.GetFolderItemsAsync(
            projectId, templatesFolder.Id, accessToken, cancellationToken);

        var templates = items
            .Where(i => i.Type == ItemType.File)
            .Select(i => new DeliverableTemplateDto(
                ItemId: i.Id,
                Name: i.Name,
                ItemUrn: i.ItemUrn,
                Extension: Path.GetExtension(i.Name) ?? string.Empty,
                Size: i.Size,
                ModifiedAt: i.ModifiedAt))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (templates.Count == 0)
        {
            _logger.LogWarning(
                "Template folder '{FolderName}' in project {ProjectId} contains no files",
                templatesFolder.Name, projectId);
        }
        else
        {
            _logger.LogInformation(
                "Found {Count} template(s) in '{FolderName}': [{Templates}]",
                templates.Count, templatesFolder.Name, string.Join(", ", templates.Select(t => t.Name)));
        }

        return templates;
    }
}
