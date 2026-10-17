namespace DigitalMIDP.Tests.Application.UseCases;

using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.UseCases;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

public class OpenFileLocallyUseCaseTests
{
    private readonly IDesktopConnectorService _mockDesktopConnectorService;
    private readonly ILogger<OpenFileLocallyUseCase> _mockLogger;
    private readonly OpenFileLocallyUseCase _useCase;

    public OpenFileLocallyUseCaseTests()
    {
        _mockDesktopConnectorService = Substitute.For<IDesktopConnectorService>();
        _mockLogger = Substitute.For<ILogger<OpenFileLocallyUseCase>>();
        _useCase = new OpenFileLocallyUseCase(_mockDesktopConnectorService, _mockLogger);
    }

    #region Input Validation Tests

    [Fact]
    public async Task ExecuteAsync_WithNullItem_ThrowsArgumentNullException()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _useCase.ExecuteAsync(null!, "Hub", "Project", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_WithNullHubName_ThrowsArgumentNullException()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _useCase.ExecuteAsync(item, null!, "Project", CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_WithNullProjectName_ThrowsArgumentNullException()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _useCase.ExecuteAsync(item, "Hub", null!, CancellationToken.None));
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", cts.Token)
            .Returns(Task.FromCanceled<FileOpenResult>(cts.Token));

        // Assert.ThrowsAsync requires an exact type match; awaiting a canceled Task throws the
        // derived TaskCanceledException, not the base type, so ThrowsAnyAsync is used instead.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _useCase.ExecuteAsync(item, "Hub", "Project", cts.Token));
    }

    #endregion

    #region Missing PathInProject Tests

    [Fact]
    public async Task ExecuteAsync_WhenPathInProjectIsNull_ReturnsErrorWithoutFallback()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = null,
            WebViewLink = "http://example.com/test"
        };

        var result = await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.LocalPath.Should().BeNull();
        result.FallbackWebUrl.Should().BeNull(); // No fallback anymore
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPathInProjectIsEmpty_ReturnsErrorWithoutFallback()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "",
            WebViewLink = "http://example.com/test"
        };

        var result = await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FallbackWebUrl.Should().BeNull(); // No fallback anymore
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPathInProjectIsWhitespace_ReturnsErrorWithoutFallback()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "   ",
            WebViewLink = "http://example.com/test"
        };

        var result = await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FallbackWebUrl.Should().BeNull(); // No fallback anymore
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Successful Local Open Tests

    [Fact]
    public async Task ExecuteAsync_WhenDesktopConnectorSucceeds_ReturnsSuccessResult()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        var successResult = new FileOpenResult(
            Success: true,
            LocalPath: "C:\\Users\\Test\\DC\\ACCDocs\\Hub\\Project\\Project Files\\folder\\test.txt",
            ErrorMessage: null,
            FallbackWebUrl: null);

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        var result = await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        result.Should().Be(successResult);
        result.Success.Should().BeTrue();
        result.LocalPath.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Error-Only Tests

    [Fact]
    public async Task ExecuteAsync_WhenDesktopConnectorFails_ReturnsErrorWithoutFallback()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        var failureResult = new FileOpenResult(
            Success: false,
            LocalPath: null,
            ErrorMessage: "File not found locally",
            FallbackWebUrl: null);

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureResult));

        var result = await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.LocalPath.Should().BeNull();
        result.FallbackWebUrl.Should().BeNull(); // No fallback anymore
        result.ErrorMessage.Should().Be("File not found locally");
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_ReturnsErrorWithoutFallback()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FileOpenResult>(new InvalidOperationException("Test error")));

        var result = await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FallbackWebUrl.Should().BeNull(); // No fallback anymore
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task ExecuteAsync_LogsOperationEntry()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FileOpenResult(false, null, "Test", "http://example.com/test")));

        await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString().Contains("OpenFileLocallyUseCase: Starting")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_LogsSuccessResult()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        var successResult = new FileOpenResult(
            Success: true,
            LocalPath: "C:\\path\\to\\file.txt",
            ErrorMessage: null,
            FallbackWebUrl: null);

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(successResult));

        await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString().Contains("File opened successfully")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_LogsFallbackWhenLocalOpenFails()
    {
        var item = new ItemDto
        {
            Id = "test",
            Name = "test.txt",
            Type = ItemType.File,
            PathInProject = "folder",
            WebViewLink = "http://example.com/test"
        };

        var failureResult = new FileOpenResult(
            Success: false,
            LocalPath: null,
            ErrorMessage: "File not found",
            FallbackWebUrl: null);

        _mockDesktopConnectorService
            .OpenFileAsync("Hub", "Project", "folder\\test.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failureResult));

        await _useCase.ExecuteAsync(item, "Hub", "Project", CancellationToken.None);

        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(s => s.ToString().Contains("Local file open failed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception, string>>());
    }

    #endregion
}
