namespace DigitalMIDP.Infrastructure.DesktopConnector;

using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;

/// <summary>
/// Service for detecting and interacting with Autodesk Desktop Connector.
/// Handles installation detection, synced project validation, local path resolution, and file opening.
/// </summary>
public class DesktopConnectorService : IDesktopConnectorService
{
    private const string RegistryKeyPath64Bit = @"SOFTWARE\Autodesk\Desktop Connector";
    private const string RegistryKeyPath32Bit = @"SOFTWARE\WOW6432Node\Autodesk\Desktop Connector";
    private const string SyncRootPathDefault = "DC\\ACCDocs";
    private const string ProjectFilesFolderName = "Project Files";

    private readonly ILogger<DesktopConnectorService> _logger;
    private readonly string _userProfilePath;

    public DesktopConnectorService(ILogger<DesktopConnectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <inheritdoc/>
    public async Task<DesktopConnectorInfo?> DetectInstallationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Detecting Desktop Connector installation...");

        var info = DetectFromRegistry();

        if (info?.IsInstalled == true)
        {
            _logger.LogInformation(
                "Desktop Connector detected. Version: {Version}, InstallPath: {InstallPath}",
                info.Version ?? "Unknown",
                info.BaseInstallPath ?? "Unknown");
        }
        else
        {
            _logger.LogInformation("Desktop Connector not detected on this system.");
        }

        return await Task.FromResult(info);
    }

    /// <inheritdoc/>
    public async Task<SyncedProject?> GetSyncedProjectAsync(
        string hubName,
        string projectName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(hubName);
        ArgumentNullException.ThrowIfNull(projectName);

        _logger.LogInformation("Checking if project '{ProjectName}' in hub '{HubName}' is synced...", projectName, hubName);

        var projectPath = ComputeProjectPath(hubName, projectName);

        if (!Directory.Exists(projectPath))
        {
            _logger.LogInformation("Project path does not exist: {Path}", projectPath);
            return null;
        }

        var projectFilesPath = Path.Combine(projectPath, ProjectFilesFolderName);

        if (!Directory.Exists(projectFilesPath))
        {
            _logger.LogInformation("Project Files folder not found: {Path}", projectFilesPath);
            return null;
        }

        var syncedProject = new SyncedProject(
            HubName: hubName,
            ProjectName: projectName,
            LocalPath: projectPath,
            LastSyncTime: GetLastModificationTime(projectFilesPath),
            Status: DetermineSyncStatus(projectFilesPath));

        _logger.LogInformation(
            "Project synced. LocalPath: {LocalPath}, Status: {Status}",
            projectPath,
            syncedProject.Status);

        return await Task.FromResult(syncedProject);
    }

    /// <inheritdoc/>
    public async Task<string?> ResolveLocalFilePathAsync(
        string hubName,
        string projectName,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(hubName);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(relativePath);

        _logger.LogInformation(
            "Resolving local file path for {RelativePath} in project '{ProjectName}'",
            relativePath,
            projectName);

        var projectPath = ComputeProjectPath(hubName, projectName);
        var projectFilesPath = Path.Combine(projectPath, ProjectFilesFolderName);

        if (!Directory.Exists(projectFilesPath))
        {
            _logger.LogInformation("Project not synced or Project Files folder missing: {Path}", projectFilesPath);
            return null;
        }

        // Normalize the relative path - remove leading slashes and "Project Files" prefix if present
        var normalizedPath = relativePath.TrimStart('/', '\\');

        // If the path starts with "Project Files", remove it since we're already in that directory
        if (normalizedPath.StartsWith("Project Files", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Substring("Project Files".Length).TrimStart('/', '\\');
        }

        var fullPath = Path.Combine(projectFilesPath, normalizedPath);

        // Normalize path separators and resolve any relative path components
        fullPath = Path.GetFullPath(fullPath);

        // Security: Ensure the resolved path is within the project files directory
        var normalizedProjectFilesPath = Path.GetFullPath(projectFilesPath);
        if (!fullPath.StartsWith(normalizedProjectFilesPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Resolved path is outside project directory. Requested: {RelativePath}, Resolved: {FullPath}, ProjectFilesPath: {ProjectFilesPath}",
                relativePath,
                fullPath,
                normalizedProjectFilesPath);
            return null;
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogInformation("File does not exist locally: {FullPath}", fullPath);
            return null;
        }

        _logger.LogInformation("Local file path resolved: {FullPath}", fullPath);
        return await Task.FromResult(fullPath);
    }

    /// <inheritdoc/>
    public async Task<FileOpenResult> OpenFileAsync(
        string hubName,
        string projectName,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(hubName);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(relativePath);

        _logger.LogInformation(
            "Attempting to open file: {RelativePath} in project '{ProjectName}' from hub '{HubName}'",
            relativePath,
            projectName,
            hubName);

        var localFilePath = await ResolveLocalFilePathAsync(hubName, projectName, relativePath, cancellationToken);

        if (string.IsNullOrEmpty(localFilePath))
        {
            _logger.LogWarning(
                "Could not resolve local file path. Hub: {HubName}, Project: {ProjectName}, RelativePath: {RelativePath}",
                hubName,
                projectName,
                relativePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: "File is not available locally in Desktop Connector. Is Desktop connector running and have you selected the project you want to access?",
                FallbackWebUrl: null);
        }

        try
        {
            _logger.LogInformation("Opening file locally: {FilePath}", localFilePath);

            var processInfo = new ProcessStartInfo
            {
                FileName = localFilePath,
                UseShellExecute = true
            };

            Process.Start(processInfo);

            _logger.LogInformation("File opened successfully: {FilePath}", localFilePath);

            return new FileOpenResult(
                Success: true,
                LocalPath: localFilePath,
                ErrorMessage: null,
                FallbackWebUrl: null);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "File not found: {FilePath}", localFilePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: "File not found on local system.",
                FallbackWebUrl: null);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to file: {FilePath}", localFilePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: "Access denied to file.",
                FallbackWebUrl: null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "Windows error opening file: {FilePath}", localFilePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: "Could not open file with default application.",
                FallbackWebUrl: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening file: {FilePath}", localFilePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: $"Unexpected error: {ex.Message}",
                FallbackWebUrl: null);
        }
    }

    /// <inheritdoc/>
    public async Task<FileOpenResult> OpenFileLocationAsync(
        string hubName,
        string projectName,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(hubName);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(relativePath);

        _logger.LogInformation(
            "Attempting to open file location: {RelativePath} in project '{ProjectName}' from hub '{HubName}'",
            relativePath,
            projectName,
            hubName);

        var localFilePath = await ResolveLocalFilePathAsync(hubName, projectName, relativePath, cancellationToken);

        if (string.IsNullOrEmpty(localFilePath))
        {
            _logger.LogWarning(
                "Could not resolve local file path for location. Hub: {HubName}, Project: {ProjectName}, RelativePath: {RelativePath}",
                hubName,
                projectName,
                relativePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: "File is not available locally in Desktop Connector. Is Desktop Connector running and have you selected the project you want to access?",
                FallbackWebUrl: null);
        }

        try
        {
            _logger.LogInformation("Opening file location: {FilePath}", localFilePath);

            var processInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{localFilePath}\"",
                UseShellExecute = true
            };

            Process.Start(processInfo);

            _logger.LogInformation("File location opened successfully: {FilePath}", localFilePath);

            return new FileOpenResult(
                Success: true,
                LocalPath: localFilePath,
                ErrorMessage: null,
                FallbackWebUrl: null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning(ex, "Windows error opening file location: {FilePath}", localFilePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: "Could not open file location in Explorer.",
                FallbackWebUrl: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening file location: {FilePath}", localFilePath);
            return new FileOpenResult(
                Success: false,
                LocalPath: null,
                ErrorMessage: $"Unexpected error: {ex.Message}",
                FallbackWebUrl: null);
        }
    }

    /// <summary>
    /// Detects Desktop Connector installation from Windows registry.
    /// </summary>
    private DesktopConnectorInfo? DetectFromRegistry()
    {
        // Try 64-bit registry first
        using (var regKey = Registry.LocalMachine.OpenSubKey(RegistryKeyPath64Bit))
        {
            if (regKey != null)
            {
                return ExtractInfoFromRegistry(regKey);
            }
        }

        // Fall back to 32-bit registry (WOW6432Node)
        using (var regKey = Registry.LocalMachine.OpenSubKey(RegistryKeyPath32Bit))
        {
            if (regKey != null)
            {
                return ExtractInfoFromRegistry(regKey);
            }
        }

        return new DesktopConnectorInfo(IsInstalled: false, Version: null, BaseInstallPath: null, SyncRootPath: null);
    }

    /// <summary>
    /// Extracts Desktop Connector information from a registry key.
    /// </summary>
    private DesktopConnectorInfo ExtractInfoFromRegistry(RegistryKey regKey)
    {
        var version = regKey.GetValue("Version") as string;
        var installPath = regKey.GetValue("InstallPath") as string;

        var syncRootPath = ComputeSyncRootPath();
        var syncRootExists = !string.IsNullOrEmpty(syncRootPath) && Directory.Exists(syncRootPath);

        return new DesktopConnectorInfo(
            IsInstalled: true,
            Version: version,
            BaseInstallPath: installPath,
            SyncRootPath: syncRootExists ? syncRootPath : null);
    }

    /// <summary>
    /// Computes the project path based on hub name and project name.
    /// Default path: %UserProfile%\DC\ACCDocs\{HubName}\{ProjectName}
    /// </summary>
    private string ComputeProjectPath(string hubName, string projectName)
    {
        return Path.Combine(_userProfilePath, SyncRootPathDefault, hubName, projectName);
    }

    /// <summary>
    /// Computes the Desktop Connector sync root path.
    /// Default path: %UserProfile%\DC\ACCDocs
    /// </summary>
    private string ComputeSyncRootPath()
    {
        return Path.Combine(_userProfilePath, SyncRootPathDefault);
    }

    /// <summary>
    /// Determines the sync status of a project based on the Project Files folder state.
    /// </summary>
    /// <summary>
    /// Folder presence is the only reliable local signal available - Desktop Connector exposes
    /// no live sync-progress API, and a folder mtime cannot distinguish "still syncing" from
    /// "finished syncing a minute ago". So this reports Synced whenever the folder exists;
    /// last-activity time is surfaced separately via <see cref="SyncedProject.LastSyncTime"/>
    /// for the caller to display (e.g. in a tooltip) instead of implying a progress state we
    /// cannot actually observe.
    /// </summary>
    private SyncStatus DetermineSyncStatus(string projectFilesPath)
    {
        return Directory.Exists(projectFilesPath) ? SyncStatus.Synced : SyncStatus.NotSynced;
    }

    /// <summary>
    /// Gets the last modification time of a directory.
    /// </summary>
    private DateTime? GetLastModificationTime(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                return new DirectoryInfo(directoryPath).LastWriteTimeUtc;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting last modification time for {DirectoryPath}", directoryPath);
        }

        return null;
    }
}
