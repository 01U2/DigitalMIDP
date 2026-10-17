namespace DigitalMIDP.Application.UseCases;

using Dtos;

/// <summary>
/// Use case for retrieving Desktop Connector installation status.
/// </summary>
public interface IGetDesktopConnectorStatusUseCase
{
    /// <summary>
    /// Detects and returns Desktop Connector installation status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>DesktopConnectorInfo with installation status and metadata.</returns>
    Task<DesktopConnectorInfo> ExecuteAsync(CancellationToken cancellationToken);
}
