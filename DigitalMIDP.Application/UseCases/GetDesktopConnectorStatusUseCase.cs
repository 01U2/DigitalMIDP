namespace DigitalMIDP.Application.UseCases;

using Microsoft.Extensions.Logging;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;

/// <summary>
/// Use case implementation for detecting Desktop Connector installation status.
/// </summary>
public class GetDesktopConnectorStatusUseCase : IGetDesktopConnectorStatusUseCase
{
    private readonly IDesktopConnectorService _desktopConnectorService;
    private readonly ILogger<GetDesktopConnectorStatusUseCase> _logger;

    public GetDesktopConnectorStatusUseCase(
        IDesktopConnectorService desktopConnectorService,
        ILogger<GetDesktopConnectorStatusUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(desktopConnectorService);
        ArgumentNullException.ThrowIfNull(logger);

        _desktopConnectorService = desktopConnectorService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DesktopConnectorInfo> ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GetDesktopConnectorStatusUseCase: Detecting Desktop Connector installation");

        try
        {
            var info = await _desktopConnectorService.DetectInstallationAsync(cancellationToken);

            if (info == null)
            {
                _logger.LogWarning("GetDesktopConnectorStatusUseCase: Desktop Connector detection returned null");
                return new DesktopConnectorInfo(
                    IsInstalled: false,
                    Version: null,
                    BaseInstallPath: null,
                    SyncRootPath: null);
            }

            if (info.IsInstalled)
            {
                _logger.LogInformation(
                    "GetDesktopConnectorStatusUseCase: Desktop Connector is installed. Version: {Version}",
                    info.Version ?? "Unknown");
            }
            else
            {
                _logger.LogInformation("GetDesktopConnectorStatusUseCase: Desktop Connector is not installed");
            }

            return info;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "GetDesktopConnectorStatusUseCase: Operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetDesktopConnectorStatusUseCase: Unexpected error detecting Desktop Connector");

            // Return a "not installed" status on error to allow graceful degradation
            return new DesktopConnectorInfo(
                IsInstalled: false,
                Version: null,
                BaseInstallPath: null,
                SyncRootPath: null);
        }
    }
}
