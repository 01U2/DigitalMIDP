namespace DigitalMIDP.Application.UseCases;

using Microsoft.Extensions.Logging;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;

/// <summary>
/// Use case implementation for revealing a file's local folder location. Errors are returned to
/// the caller for handling.
/// </summary>
public class OpenFileLocationUseCase : IOpenFileLocationUseCase
{
    private readonly IDesktopConnectorService _desktopConnectorService;
    private readonly ILogger<OpenFileLocationUseCase> _logger;

    public OpenFileLocationUseCase(
        IDesktopConnectorService desktopConnectorService,
        ILogger<OpenFileLocationUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(desktopConnectorService);
        ArgumentNullException.ThrowIfNull(logger);

        _desktopConnectorService = desktopConnectorService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<FileOpenResult> ExecuteAsync(
        ItemDto item,
        string hubName,
        string projectName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(hubName);
        ArgumentNullException.ThrowIfNull(projectName);

        _logger.LogInformation(
            "OpenFileLocationUseCase: Starting file location open for item '{ItemName}' in project '{ProjectName}'",
            item.Name,
            projectName);

        try
        {
            if (string.IsNullOrWhiteSpace(item.PathInProject))
            {
                _logger.LogWarning(
                    "OpenFileLocationUseCase: Item '{ItemName}' has no pathInProject. Cannot open location.",
                    item.Name);

                return new FileOpenResult(
                    Success: false,
                    LocalPath: null,
                    ErrorMessage: "File path not available. Cannot open file location.",
                    FallbackWebUrl: null);
            }

            var relativeFilePath = Path.Combine(item.PathInProject, item.Name);

            var result = await _desktopConnectorService.OpenFileLocationAsync(
                hubName,
                projectName,
                relativeFilePath,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "OpenFileLocationUseCase: File location opened successfully. LocalPath: {LocalPath}",
                    result.LocalPath);

                return result;
            }

            _logger.LogInformation(
                "OpenFileLocationUseCase: Opening file location failed. Reason: {Reason}",
                result.ErrorMessage);

            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: result.ErrorMessage,
                FallbackWebUrl: null);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "OpenFileLocationUseCase: Operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "OpenFileLocationUseCase: Unexpected error opening location for '{ItemName}'",
                item.Name);

            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: $"Unexpected error: {ex.Message}",
                FallbackWebUrl: null);
        }
    }
}
