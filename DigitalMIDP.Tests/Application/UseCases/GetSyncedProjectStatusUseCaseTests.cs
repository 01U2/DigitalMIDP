using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

/// <summary>
/// Unit tests for GetSyncedProjectStatusUseCase, covering the project sync status feature: the
/// happy (synced) path, the not-synced path, error handling, cancellation, and argument validation.
/// </summary>
public sealed class GetSyncedProjectStatusUseCaseTests
{
    private readonly IDesktopConnectorService _desktopConnectorService;
    private readonly ILogger<GetSyncedProjectStatusUseCase> _logger;
    private readonly GetSyncedProjectStatusUseCase _useCase;

    public GetSyncedProjectStatusUseCaseTests()
    {
        _desktopConnectorService = Substitute.For<IDesktopConnectorService>();
        _logger = Substitute.For<ILogger<GetSyncedProjectStatusUseCase>>();
        _useCase = new GetSyncedProjectStatusUseCase(_desktopConnectorService, _logger);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProjectIsSynced_ReturnsServiceResultUnchanged()
    {
        var synced = new SyncedProject("Hub", "Project", @"C:\Local\Path", DateTime.UtcNow, SyncStatus.Synced);
        _desktopConnectorService
            .GetSyncedProjectAsync("Hub", "Project", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SyncedProject?>(synced));

        var result = await _useCase.ExecuteAsync("Hub", "Project", CancellationToken.None);

        result.Should().Be(synced);
        result.Status.Should().Be(SyncStatus.Synced);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProjectIsNotSynced_ReturnsNotSyncedResult()
    {
        _desktopConnectorService
            .GetSyncedProjectAsync("Hub", "Project", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SyncedProject?>(null));

        var result = await _useCase.ExecuteAsync("Hub", "Project", CancellationToken.None);

        result.HubName.Should().Be("Hub");
        result.ProjectName.Should().Be("Project");
        result.LocalPath.Should().BeEmpty();
        result.LastSyncTime.Should().BeNull();
        result.Status.Should().Be(SyncStatus.NotSynced);
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceThrowsUnexpectedException_ReturnsErrorResultInsteadOfThrowing()
    {
        _desktopConnectorService
            .GetSyncedProjectAsync("Hub", "Project", Arg.Any<CancellationToken>())
            .Returns<Task<SyncedProject?>>(_ => throw new InvalidOperationException("Registry unreadable."));

        var result = await _useCase.ExecuteAsync("Hub", "Project", CancellationToken.None);

        result.HubName.Should().Be("Hub");
        result.ProjectName.Should().Be("Project");
        result.Status.Should().Be(SyncStatus.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_RethrowsOperationCanceledException()
    {
        _desktopConnectorService
            .GetSyncedProjectAsync("Hub", "Project", Arg.Any<CancellationToken>())
            .Returns<Task<SyncedProject?>>(_ => throw new OperationCanceledException());

        var act = () => _useCase.ExecuteAsync("Hub", "Project", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null, "Project")]
    [InlineData("", "Project")]
    [InlineData("   ", "Project")]
    [InlineData("Hub", null)]
    [InlineData("Hub", "")]
    [InlineData("Hub", "   ")]
    public async Task ExecuteAsync_WithMissingHubOrProjectName_ThrowsArgumentException(string? hubName, string? projectName)
    {
        var act = () => _useCase.ExecuteAsync(hubName!, projectName!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenThrough()
    {
        using var cts = new CancellationTokenSource();
        _desktopConnectorService
            .GetSyncedProjectAsync("Hub", "Project", cts.Token)
            .Returns(Task.FromResult<SyncedProject?>(null));

        await _useCase.ExecuteAsync("Hub", "Project", cts.Token);

        await _desktopConnectorService.Received(1).GetSyncedProjectAsync("Hub", "Project", cts.Token);
    }
}
