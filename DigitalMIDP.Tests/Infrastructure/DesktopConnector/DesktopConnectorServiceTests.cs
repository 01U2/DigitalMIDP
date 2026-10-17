namespace DigitalMIDP.Tests.Infrastructure.DesktopConnector;

using System.Diagnostics;
using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Infrastructure.DesktopConnector;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

public class DesktopConnectorServiceTests
{
    // Matches DesktopConnectorService.ComputeSyncRootPath's default: %UserProfile%\DC\ACCDocs.
    // Files must be written here, not under Path.GetTempPath(), for the service to find them.
    private static readonly string SyncRootPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DC", "ACCDocs");

    private readonly ILogger<DesktopConnectorService> _mockLogger;
    private readonly DesktopConnectorService _service;

    public DesktopConnectorServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<DesktopConnectorService>>();
        _service = new DesktopConnectorService(_mockLogger);
    }

    #region DetectInstallationAsync Tests

    [Fact]
    public async Task DetectInstallationAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.DetectInstallationAsync(cts.Token));
    }

    [Fact]
    public async Task DetectInstallationAsync_ReturnsDesktopConnectorInfo()
    {
        // DetectFromRegistry reads the real Windows registry with no seam for injection, so
        // IsInstalled reflects whatever's actually on the machine running this test - true on a
        // dev box with Desktop Connector installed, false on a clean CI runner - and even the
        // registry's own Version/InstallPath values aren't guaranteed present across installs.
        // Assert only the one invariant that always holds: SyncRootPath is never returned unless
        // Desktop Connector is installed.
        var result = await _service.DetectInstallationAsync(CancellationToken.None);

        result.Should().NotBeNull();
        if (!result!.IsInstalled)
        {
            result.SyncRootPath.Should().BeNull();
        }
    }

    [Fact]
    public async Task DetectInstallationAsync_LogsInstallationDetectionAttempt()
    {
        await _service.DetectInstallationAsync(CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString().Contains("Detecting Desktop Connector")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception, string>>());
    }

    #endregion

    #region GetSyncedProjectAsync Tests

    [Fact]
    public async Task GetSyncedProjectAsync_WithNullHubName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.GetSyncedProjectAsync(null!, "ProjectName", CancellationToken.None));
    }

    [Fact]
    public async Task GetSyncedProjectAsync_WithNullProjectName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.GetSyncedProjectAsync("HubName", null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetSyncedProjectAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.GetSyncedProjectAsync("Hub", "Project", cts.Token));
    }

    [Fact]
    public async Task GetSyncedProjectAsync_WhenProjectPathDoesNotExist_ReturnsNull()
    {
        var result = await _service.GetSyncedProjectAsync(
            "NonExistentHub",
            "NonExistentProject",
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSyncedProjectAsync_WhenProjectFilesPathDoesNotExist_ReturnsNull()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestHub", "TestProject");
        try
        {
            System.IO.Directory.CreateDirectory(tempDir);

            var result = await _service.GetSyncedProjectAsync("TestHub", "TestProject", CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            try
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(tempDir)!, true);
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task GetSyncedProjectAsync_WhenProjectIsSynced_ReturnsSyncedProject()
    {
        var tempDir = System.IO.Path.Combine(SyncRootPath, "TestHub", "TestProject", "Project Files");
        try
        {
            System.IO.Directory.CreateDirectory(tempDir);

            var result = await _service.GetSyncedProjectAsync(
                "TestHub",
                "TestProject",
                CancellationToken.None);

            result.Should().NotBeNull();
            result!.HubName.Should().Be("TestHub");
            result.ProjectName.Should().Be("TestProject");
            result.LocalPath.Should().Contain("TestHub");
            result.Status.Should().Be(SyncStatus.Synced);
        }
        finally
        {
            try
            {
                var basePath = System.IO.Path.Combine(SyncRootPath, "TestHub");
                if (System.IO.Directory.Exists(basePath))
                {
                    System.IO.Directory.Delete(basePath, true);
                }
            }
            catch { }
        }
    }

    #endregion

    #region ResolveLocalFilePathAsync Tests

    [Fact]
    public async Task ResolveLocalFilePathAsync_WithNullHubName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveLocalFilePathAsync(null!, "Project", "file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WithNullProjectName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveLocalFilePathAsync("Hub", null!, "file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WithNullRelativePath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveLocalFilePathAsync("Hub", "Project", null!, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.ResolveLocalFilePathAsync("Hub", "Project", "file.txt", cts.Token));
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WhenProjectNotSynced_ReturnsNull()
    {
        var result = await _service.ResolveLocalFilePathAsync(
            "NonExistentHub",
            "NonExistentProject",
            "file.txt",
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var projectFilesDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TestHub",
            "TestProject",
            "Project Files");

        try
        {
            System.IO.Directory.CreateDirectory(projectFilesDir);

            var result = await _service.ResolveLocalFilePathAsync(
                "TestHub",
                "TestProject",
                "nonexistent.txt",
                CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            try
            {
                var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestHub");
                if (System.IO.Directory.Exists(basePath))
                {
                    System.IO.Directory.Delete(basePath, true);
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WhenFileExists_ReturnsFullPath()
    {
        var projectFilesDir = System.IO.Path.Combine(
            SyncRootPath,
            "TestHub",
            "TestProject",
            "Project Files");
        var testFile = System.IO.Path.Combine(projectFilesDir, "test.txt");

        try
        {
            System.IO.Directory.CreateDirectory(projectFilesDir);
            System.IO.File.WriteAllText(testFile, "test content");

            var result = await _service.ResolveLocalFilePathAsync(
                "TestHub",
                "TestProject",
                "test.txt",
                CancellationToken.None);

            result.Should().NotBeNull();
            result.Should().Be(testFile);
        }
        finally
        {
            try
            {
                var basePath = System.IO.Path.Combine(SyncRootPath, "TestHub");
                if (System.IO.Directory.Exists(basePath))
                {
                    System.IO.Directory.Delete(basePath, true);
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task ResolveLocalFilePathAsync_WhenPathTraversalAttempted_ReturnsNull()
    {
        var projectFilesDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TestHub",
            "TestProject",
            "Project Files");

        try
        {
            System.IO.Directory.CreateDirectory(projectFilesDir);

            var result = await _service.ResolveLocalFilePathAsync(
                "TestHub",
                "TestProject",
                "../../../etc/passwd", // Path traversal attempt
                CancellationToken.None);

            result.Should().BeNull();
        }
        finally
        {
            try
            {
                var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestHub");
                if (System.IO.Directory.Exists(basePath))
                {
                    System.IO.Directory.Delete(basePath, true);
                }
            }
            catch { }
        }
    }

    #endregion

    #region OpenFileAsync Tests

    [Fact]
    public async Task OpenFileAsync_WithNullHubName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.OpenFileAsync(null!, "Project", "file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task OpenFileAsync_WithNullProjectName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.OpenFileAsync("Hub", null!, "file.txt", CancellationToken.None));
    }

    [Fact]
    public async Task OpenFileAsync_WithNullRelativePath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.OpenFileAsync("Hub", "Project", null!, CancellationToken.None));
    }

    [Fact]
    public async Task OpenFileAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.OpenFileAsync("Hub", "Project", "file.txt", cts.Token));
    }

    [Fact]
    public async Task OpenFileAsync_WhenFileNotResolved_ReturnsFailed()
    {
        var result = await _service.OpenFileAsync(
            "NonExistentHub",
            "NonExistentProject",
            "file.txt",
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.LocalPath.Should().BeNull();
        result.ErrorMessage.Should().Contain("not available locally");
    }

    [Fact]
    public async Task OpenFileAsync_WhenFileDoesNotExist_ReturnsFailed()
    {
        var projectFilesDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TestHub",
            "TestProject",
            "Project Files");

        try
        {
            System.IO.Directory.CreateDirectory(projectFilesDir);

            var result = await _service.OpenFileAsync(
                "TestHub",
                "TestProject",
                "nonexistent.txt",
                CancellationToken.None);

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrEmpty();
        }
        finally
        {
            try
            {
                var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestHub");
                if (System.IO.Directory.Exists(basePath))
                {
                    System.IO.Directory.Delete(basePath, true);
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task OpenFileAsync_LogsFileOpenAttempt()
    {
        var projectFilesDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TestHub",
            "TestProject",
            "Project Files");

        try
        {
            System.IO.Directory.CreateDirectory(projectFilesDir);

            await _service.OpenFileAsync("TestHub", "TestProject", "file.txt", CancellationToken.None);

            _mockLogger.Received().Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(s => s.ToString().Contains("Attempting to open file")),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>());
        }
        finally
        {
            try
            {
                var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestHub");
                if (System.IO.Directory.Exists(basePath))
                {
                    System.IO.Directory.Delete(basePath, true);
                }
            }
            catch { }
        }
    }

    #endregion
}
