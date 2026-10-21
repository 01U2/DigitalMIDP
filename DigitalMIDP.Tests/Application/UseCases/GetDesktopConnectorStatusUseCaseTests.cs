namespace DigitalMIDP.Tests.Application.UseCases;

using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

public class GetDesktopConnectorStatusUseCaseTests
{
    private readonly IDesktopConnectorService _mockDesktopConnectorService;
    private readonly ILogger<GetDesktopConnectorStatusUseCase> _mockLogger;
    private readonly GetDesktopConnectorStatusUseCase _useCase;

    public GetDesktopConnectorStatusUseCaseTests()
    {
        _mockDesktopConnectorService = Substitute.For<IDesktopConnectorService>();
        _mockLogger = Substitute.For<ILogger<GetDesktopConnectorStatusUseCase>>();
        _useCase = new GetDesktopConnectorStatusUseCase(_mockDesktopConnectorService, _mockLogger);
    }

    #region Cancellation Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockDesktopConnectorService
            .DetectInstallationAsync(cts.Token)
            .Returns(Task.FromCanceled<DesktopConnectorInfo?>(cts.Token));

        // Assert.ThrowsAsync requires an exact type match; awaiting a canceled Task throws the
        // derived TaskCanceledException, not the base type, so ThrowsAnyAsync is used instead.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _useCase.ExecuteAsync(cts.Token));
    }

    #endregion

    #region Installation Detected Tests

    [Fact]
    public async Task ExecuteAsync_WhenDesktopConnectorInstalled_ReturnsInstalledInfo()
    {
        var expectedInfo = new DesktopConnectorInfo(
            IsInstalled: true,
            Version: "1.0.0",
            BaseInstallPath: "C:\\Program Files\\Autodesk\\Desktop Connector",
            SyncRootPath: "C:\\Users\\Test\\DC\\ACCDocs");

        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(expectedInfo));

        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.IsInstalled.Should().BeTrue();
        result.Version.Should().Be("1.0.0");
        result.BaseInstallPath.Should().NotBeNullOrEmpty();
        result.SyncRootPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDesktopConnectorInstalled_LogsInstallationInfo()
    {
        var expectedInfo = new DesktopConnectorInfo(
            IsInstalled: true,
            Version: "1.0.0",
            BaseInstallPath: "C:\\Program Files\\Autodesk\\Desktop Connector",
            SyncRootPath: "C:\\Users\\Test\\DC\\ACCDocs");

        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(expectedInfo));

        await _useCase.ExecuteAsync(CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString()!.Contains("installed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Installation Not Detected Tests

    [Fact]
    public async Task ExecuteAsync_WhenDesktopConnectorNotInstalled_ReturnsNotInstalledInfo()
    {
        var expectedInfo = new DesktopConnectorInfo(
            IsInstalled: false,
            Version: null,
            BaseInstallPath: null,
            SyncRootPath: null);

        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(expectedInfo));

        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.IsInstalled.Should().BeFalse();
        result.Version.Should().BeNull();
        result.BaseInstallPath.Should().BeNull();
        result.SyncRootPath.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDesktopConnectorNotInstalled_LogsNotInstalledMessage()
    {
        var expectedInfo = new DesktopConnectorInfo(
            IsInstalled: false,
            Version: null,
            BaseInstallPath: null,
            SyncRootPath: null);

        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(expectedInfo));

        await _useCase.ExecuteAsync(CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString()!.Contains("not installed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Null Return Tests

    [Fact]
    public async Task ExecuteAsync_WhenServiceReturnsNull_ReturnsNotInstalledInfo()
    {
        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(null));

        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.IsInstalled.Should().BeFalse();
        result.Version.Should().BeNull();
        result.BaseInstallPath.Should().BeNull();
        result.SyncRootPath.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceReturnsNull_LogsWarning()
    {
        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(null));

        await _useCase.ExecuteAsync(CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString()!.Contains("null")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ReturnsNotInstalledInfo()
    {
        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DesktopConnectorInfo?>(new InvalidOperationException("Test error")));

        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.IsInstalled.Should().BeFalse();
        result.Version.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_LogsError()
    {
        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DesktopConnectorInfo?>(new InvalidOperationException("Test error")));

        await _useCase.ExecuteAsync(CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString()!.Contains("Unexpected error")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_GracefullyDegrades()
    {
        // Verify that when the service throws, the use case doesn't propagate
        // the exception but instead returns a "not installed" state
        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DesktopConnectorInfo?>(new AccessViolationException("Test error")));

        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.IsInstalled.Should().BeFalse();
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task ExecuteAsync_LogsOperationEntry()
    {
        _mockDesktopConnectorService
            .DetectInstallationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DesktopConnectorInfo?>(
                new DesktopConnectorInfo(false, null, null, null)));

        await _useCase.ExecuteAsync(CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString()!.Contains("GetDesktopConnectorStatusUseCase: Detecting")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion
}
