using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Resolves the project's CDE folder layout - the ISO 19650 lifecycle folders, the template
/// repository, and the discipline subfolders under WIP.
///
/// Everything is resolved by name at runtime. Folder ids are project-specific, and the discipline
/// list cannot be known ahead of time: projects can carry 10 or more, and the set differs per
/// project, so it is derived from the naming convention's role field rather than configured.
/// </summary>
public interface ICdeStructureUseCase
{
    /// <summary>
    /// Resolves the full CDE structure for a project.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="projectId">The ID of the project (Data Management API format with "b." prefix).</param>
    /// <param name="accessToken">The OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The resolved structure. Missing folders are reported as null/non-existent rather than raising -
    /// a project part-way through setup is a state the caller should be able to show the user, not a
    /// failure.
    /// </returns>
    Task<CdeStructureDto> GetStructureAsync(
        string hubId,
        string projectId,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the project's naming convention, cached the same way and for the same reason as
    /// <see cref="GetStructureAsync"/>.
    ///
    /// Every consumer of the naming convention (the creation wizard, the create-deliverable use case,
    /// and this use case's own discipline resolution) should call this rather than
    /// <c>IDataManagementService.GetNamingConventionAsync</c> directly - resolving it costs several
    /// HTTP round trips, and a single pass through the wizard asks for it more than once.
    /// </summary>
    /// <param name="hubId">The ID of the hub.</param>
    /// <param name="projectId">The ID of the project.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    Task<NamingConventionDto> GetNamingConventionAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards any cached structure and naming convention for a project, so the next resolution
    /// reflects folders created since. Call this after creating a folder in the project.
    /// </summary>
    /// <param name="projectId">The ID of the project to forget.</param>
    void Invalidate(string projectId);
}
