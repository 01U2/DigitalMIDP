using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Lists the templates available in the project's template repository.
/// </summary>
public interface ITemplateCatalogUseCase
{
    /// <summary>
    /// Retrieves every template file in the project's template folder.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The available templates, ordered by name. Empty when the project has no template folder or
    /// the folder holds no files - both are states the wizard needs to report to the user, so
    /// neither raises.
    /// </returns>
    Task<IReadOnlyList<DeliverableTemplateDto>> GetTemplatesAsync(
        string hubId,
        string projectId,
        string accessToken,
        CancellationToken cancellationToken = default);
}
