extern alias runner;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Application.UseCases;
using NSubstitute;
using Xunit;
using RunnerViewModel = runner::DigitalMIDP.ManifestRunner.ViewModels.RunnerViewModel;

namespace DigitalMIDP.Tests.UI.ViewModels;

public sealed class RunnerViewModelTests
{
    private readonly IManifestRunner scheduler = Substitute.For<IManifestRunner>();
    private readonly IAuthenticationUseCase auth = Substitute.For<IAuthenticationUseCase>();
    private readonly ILogoutUseCase logout = Substitute.For<ILogoutUseCase>();
    private readonly IFileListingUseCase listing = Substitute.For<IFileListingUseCase>();
    private readonly RunnerViewModel model;
    private readonly ManifestProjectStatus row = new("hub", "project", TimeSpan.FromMinutes(10), false, null, null, null, null);
    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    public RunnerViewModelTests()
    {
        var tokens = Substitute.For<ITokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test");
        listing.GetHubsAsync("test", Arg.Any<CancellationToken>()).Returns(new GetHubsResponseDto
            { Hubs = [new HubDto { Id = "hub", Name = "Hub", Type = "test" }] });
        listing.GetProjectsAsync("hub", "test", Arg.Any<CancellationToken>()).Returns(new GetProjectsResponseDto
            { Projects = [new ProjectDto { Id = "project", Name = "Project" }] });
        scheduler.GetSnapshot().Returns(new[] { row });
        model = new(auth, logout, tokens, listing, scheduler, new ImmediateContext());
    }

    [Fact]
    public async Task EnrollAndManageSelectedProject()
    {
        Assert.False(model.Enroll.CanExecute(null));
        await model.SignIn.ExecuteAsync(null);
        await model.LoadProjects.ExecuteAsync(null);
        await model.Enroll.ExecuteAsync(null);
        await scheduler.Received(1).EnableAsync("hub", "project", TimeSpan.FromMinutes(10));
        model.SelectedRow = Assert.Single(model.Rows);
        model.IntervalMinutes = "0";
        await model.ChangeInterval.ExecuteAsync(null);
        Assert.Contains("whole minutes", model.Message);
        model.IntervalMinutes = "25";
        await model.ChangeInterval.ExecuteAsync(null);
        await scheduler.Received(1).EnableAsync("hub", "project", TimeSpan.FromMinutes(25));
        await model.CheckNow.ExecuteAsync(null);
        await scheduler.Received(1).CheckNowAsync("hub", "project", Arg.Any<CancellationToken>());
        await model.Remove.ExecuteAsync(null);
        await scheduler.Received(1).DisableAsync("hub", "project");
        model.Hub = null;
        Assert.Empty(model.Projects);
        Assert.False(model.Enroll.CanExecute(null));
    }

    [Fact]
    public async Task StatusEventUpdatesSelectionAndLogoutStopsBeforeRevocation()
    {
        await model.SignIn.ExecuteAsync(null);
        model.SelectedRow = Assert.Single(model.Rows);
        scheduler.GetSnapshot().Returns(new[] { row with { LastError = "InvalidDataException" } });
        scheduler.StatusChanged += Raise.EventWith(EventArgs.Empty);
        Assert.Equal("InvalidDataException", model.SelectedRow?.LastError);
        await model.SignOut.ExecuteAsync(null);
        Received.InOrder(() => { _ = scheduler.StopAsync(); _ = logout.ExecuteAsync(Arg.Any<CancellationToken>()); });
        Assert.False(model.SignedIn);
        Assert.Empty(model.Rows);
        Assert.Empty(model.Hubs);
        Assert.False(model.CheckNow.CanExecute(null));
    }

    [Fact]
    public async Task FailedSignInDoesNotStartScheduler()
    {
        auth.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<AuthenticationResponseDto>(new InvalidOperationException()));
        await model.SignIn.ExecuteAsync(null);
        Assert.False(model.SignedIn);
        Assert.Contains("Sign in failed", model.Message);
        await scheduler.DidNotReceive().StartAsync();
    }

    [Fact]
    public async Task CloseCancelsPendingAuthenticationAndDrainsScheduler()
    {
        static async Task<AuthenticationResponseDto> WaitForCancellation(CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        }
        auth.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(call => WaitForCancellation(call.Arg<CancellationToken>()));
        var pending = model.SignIn.ExecuteAsync(null);
        await model.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await pending;
        await scheduler.DidNotReceive().StartAsync();
        await scheduler.Received(1).StopAsync();
    }
}
