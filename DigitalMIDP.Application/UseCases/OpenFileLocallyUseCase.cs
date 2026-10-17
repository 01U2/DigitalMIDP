namespace DigitalMIDP.Application.UseCases;

using Microsoft.Extensions.Logging;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;

/// <summary>
/// Use case implementation for opening files locally. Errors are returned to the caller for handling.
/// </summary>
public class OpenFileLocallyUseCase : IOpenFileLocallyUseCase
{
    private readonly IDesktopConnectorService _desktopConnectorService;
    private readonly ILogger<OpenFileLocallyUseCase> _logger;

    public OpenFileLocallyUseCase(
        IDesktopConnectorService desktopConnectorService,
        ILogger<OpenFileLocallyUseCase> logger)
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
            "OpenFileLocallyUseCase: Starting file open for item '{ItemName}' in project '{ProjectName}'",
            item.Name,
            projectName);

        try
        {
            // Validate that the item has a path (file items should have pathInProject)
            if (string.IsNullOrWhiteSpace(item.PathInProject))
            {
                _logger.LogWarning(
                    "OpenFileLocallyUseCase: Item '{ItemName}' has no pathInProject. Cannot open locally.",
                    item.Name);

                return new FileOpenResult(
                    Success: false,
                    LocalPath: null,
                    ErrorMessage: "File path not available. Cannot open locally.",
                    FallbackWebUrl: null);
            }

            // Construct the complete relative file path by combining folder path with filename
            var relativeFilePath = Path.Combine(item.PathInProject, item.Name);

            // Attempt to open the file locally
            var result = await _desktopConnectorService.OpenFileAsync(
                hubName,
                projectName,
                relativeFilePath,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "OpenFileLocallyUseCase: File opened successfully. LocalPath: {LocalPath}",
                    result.LocalPath);

                return result;
            }

            // Local open failed, return error without fallback
            _logger.LogInformation(
                "OpenFileLocallyUseCase: Local file open failed. Reason: {Reason}",
                result.ErrorMessage);

            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: result.ErrorMessage,
                FallbackWebUrl: null);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "OpenFileLocallyUseCase: Operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "OpenFileLocallyUseCase: Unexpected error opening file '{ItemName}'",
                item.Name);

            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: $"Unexpected error: {ex.Message}",
                FallbackWebUrl: null);
        }
    }
}
