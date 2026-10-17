namespace DigitalMIDP.Application.UseCases;

using Microsoft.Extensions.Logging;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;

/// <summary>
/// Use case implementation for resolving a file's local Desktop Connector path - see
/// <see cref="IGetLocalFilePathUseCase"/>. Mirrors <see cref="OpenFileLocationUseCase"/>'s
/// path-composition and error handling, minus the Explorer launch.
/// </summary>
public class GetLocalFilePathUseCase : IGetLocalFilePathUseCase
{
    private readonly IDesktopConnectorService _desktopConnectorService;
    private readonly ILogger<GetLocalFilePathUseCase> _logger;

    public GetLocalFilePathUseCase(
        IDesktopConnectorService desktopConnectorService,
        ILogger<GetLocalFilePathUseCase> logger)
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
            "GetLocalFilePathUseCase: Resolving local path for item '{ItemName}' in project '{ProjectName}'",
            item.Name,
            projectName);

        try
        {
            if (string.IsNullOrWhiteSpace(item.PathInProject))
            {
                _logger.LogWarning(
                    "GetLocalFilePathUseCase: Item '{ItemName}' has no pathInProject. Cannot resolve local path.",
                    item.Name);

                return new FileOpenResult(
                    Success: false,
                    LocalPath: null,
                    ErrorMessage: "File path not available. Cannot resolve local path.",
                    FallbackWebUrl: null);
            }

            var relativeFilePath = Path.Combine(item.PathInProject, item.Name);

            var localPath = await _desktopConnectorService.ResolveLocalFilePathAsync(
                hubName,
                projectName,
                relativeFilePath,
                cancellationToken);

            if (string.IsNullOrEmpty(localPath))
            {
                return new FileOpenResult(
                    Success: false,
                    LocalPath: null,
                    ErrorMessage: "File is not available locally in Desktop Connector. Is Desktop Connector running and have you selected the project you want to access?",
                    FallbackWebUrl: null);
            }

            _logger.LogInformation(
                "GetLocalFilePathUseCase: Local path resolved. LocalPath: {LocalPath}",
                localPath);

            return new FileOpenResult(
                Success: true,
                LocalPath: localPath,
                ErrorMessage: null,
                FallbackWebUrl: null);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "GetLocalFilePathUseCase: Operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetLocalFilePathUseCase: Unexpected error resolving local path for '{ItemName}'",
                item.Name);

            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: $"Unexpected error: {ex.Message}",
                FallbackWebUrl: null);
        }
    }
}
