namespace DigitalMIDP.Application.UseCases;

using Dtos;

/// <summary>
/// Use case for retrieving the Desktop Connector sync status of a specific project.
/// </summary>
public interface IGetSyncedProjectStatusUseCase
{
    /// <summary>
    /// Checks whether the given project is synced locally via Desktop Connector.
    /// </summary>
    /// <param name="hubName">The hub name.</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>SyncedProject describing the local sync status.</returns>
    Task<SyncedProject> ExecuteAsync(
        string hubName,
        string projectName,
        CancellationToken cancellationToken);
}
