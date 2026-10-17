namespace DigitalMIDP.Application.UseCases;

using Microsoft.Extensions.Logging;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;

/// <summary>
/// Use case implementation for retrieving the Desktop Connector sync status of a specific project.
/// </summary>
public class GetSyncedProjectStatusUseCase : IGetSyncedProjectStatusUseCase
{
    private readonly IDesktopConnectorService _desktopConnectorService;
    private readonly ILogger<GetSyncedProjectStatusUseCase> _logger;

    public GetSyncedProjectStatusUseCase(
        IDesktopConnectorService desktopConnectorService,
        ILogger<GetSyncedProjectStatusUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(desktopConnectorService);
        ArgumentNullException.ThrowIfNull(logger);

        _desktopConnectorService = desktopConnectorService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SyncedProject> ExecuteAsync(
        string hubName,
        string projectName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        _logger.LogInformation(
            "GetSyncedProjectStatusUseCase: Checking sync status for hub {HubName}, project {ProjectName}",
            hubName,
            projectName);

        try
        {
            var syncedProject = await _desktopConnectorService.GetSyncedProjectAsync(hubName, projectName, cancellationToken);

            if (syncedProject == null)
            {
                _logger.LogInformation(
                    "GetSyncedProjectStatusUseCase: Project {ProjectName} is not synced locally",
                    projectName);

                return new SyncedProject(hubName, projectName, string.Empty, null, SyncStatus.NotSynced);
            }

            _logger.LogInformation(
                "GetSyncedProjectStatusUseCase: Project {ProjectName} sync status is {Status}",
                projectName,
                syncedProject.Status);

            return syncedProject;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "GetSyncedProjectStatusUseCase: Operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSyncedProjectStatusUseCase: Unexpected error checking sync status for project {ProjectName}",
                projectName);

            return new SyncedProject(hubName, projectName, string.Empty, null, SyncStatus.Error);
        }
    }
}
