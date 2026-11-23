using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Application.UseCases;

namespace DigitalMIDP.ManifestRunner.ViewModels;

public sealed partial class RunnerViewModel : ObservableObject
{
    private readonly IManifestRunner runner;
    private readonly SynchronizationContext ui;
    private readonly CancellationTokenSource lifetime = new();
    private Task active = Task.CompletedTask;

    [ObservableProperty] private bool signedIn;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string message = "Signed out. Sign in to resume enrolled projects.";
    [ObservableProperty] private IReadOnlyList<HubDto> hubs = [];
    [ObservableProperty] private IReadOnlyList<ProjectDto> projects = [];
    [ObservableProperty] private HubDto? hub;
    [ObservableProperty] private ProjectDto? project;
    [ObservableProperty] private string intervalMinutes = "10";
    [ObservableProperty] private IReadOnlyList<ManifestProjectStatus> rows = [];
    [ObservableProperty] private ManifestProjectStatus? selectedRow;

    public IAsyncRelayCommand SignIn { get; }
    public IAsyncRelayCommand SignOut { get; }
    public IAsyncRelayCommand LoadHubs { get; }
    public IAsyncRelayCommand LoadProjects { get; }
    public IAsyncRelayCommand Enroll { get; }
    public IAsyncRelayCommand ChangeInterval { get; }
    public IAsyncRelayCommand Remove { get; }
    public IAsyncRelayCommand CheckNow { get; }

    public RunnerViewModel(IAuthenticationUseCase auth, ILogoutUseCase logout, ITokenProvider tokens,
        IFileListingUseCase listing, IManifestRunner runner, SynchronizationContext ui)
    {
        this.runner = runner;
        this.ui = ui;
        runner.StatusChanged += StatusChanged;

        async Task DiscoverHubs(CancellationToken ct)
        {
            Hubs = (await listing.GetHubsAsync(await tokens.GetAccessTokenAsync(ct), ct)).Hubs.ToArray();
            Hub = Hubs.FirstOrDefault();
            Message = Hubs.Count == 0 ? "No hubs available for this account." : "Select a hub and load its projects.";
        }
        SignIn = Command("Sign in", () => !SignedIn, async ct =>
        {
            await auth.ExecuteAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                await runner.StartAsync();
            }
            catch
            {
                await runner.StopAsync();
                await logout.ExecuteAsync(ct);
                throw;
            }
            SignedIn = true;
            Refresh();
            await DiscoverHubs(ct);
        });
        SignOut = Command("Sign out", () => SignedIn, async ct =>
        {
            await runner.StopAsync();
            try
            {
                Message = await logout.ExecuteAsync(ct) ? "Signed out." : "Signed out locally. APS did not confirm revocation.";
            }
            finally
            {
                SignedIn = false;
                Hub = null;
                Hubs = [];
                Rows = [];
                SelectedRow = null;
            }
        });
        LoadHubs = Command("Load hubs", () => SignedIn, DiscoverHubs);
        LoadProjects = Command("Load projects", () => SignedIn && Hub != null, async ct =>
        {
            var selected = Hub!;
            var response = await listing.GetProjectsAsync(selected.Id, await tokens.GetAccessTokenAsync(ct), ct);
            if (Hub != selected) return;
            Projects = response.Projects.ToArray();
            Project = Projects.FirstOrDefault();
            Message = Project == null ? "No projects available in this hub." : "Select a project to enroll.";
        });
        Enroll = Command("Enroll project", () => SignedIn && Hub != null && Project != null,
            _ => SetIntervalAsync(Hub!.Id, Project!.Id));
        bool HasRow() => SignedIn && SelectedRow != null;
        ChangeInterval = Command("Change interval", HasRow, _ => SetIntervalAsync(SelectedRow!.HubId, SelectedRow.ProjectId));
        Remove = Command("Remove enrollment", HasRow, _ => runner.DisableAsync(SelectedRow!.HubId, SelectedRow.ProjectId));
        CheckNow = Command("Check now", HasRow, ct => runner.CheckNowAsync(SelectedRow!.HubId, SelectedRow.ProjectId, ct));
    }

    private Task SetIntervalAsync(string hubId, string projectId)
    {
        if (!int.TryParse(IntervalMinutes, out var minutes) || minutes is < 1 or > 71582)
        {
            Message = "Enter an interval from 1 to 71582 whole minutes.";
            return Task.CompletedTask;
        }
        return runner.EnableAsync(hubId, projectId, TimeSpan.FromMinutes(minutes));
    }

    private IAsyncRelayCommand Command(string label, Func<bool> allowed, Func<CancellationToken, Task> action) =>
        new AsyncRelayCommand(() => !Busy && !lifetime.IsCancellationRequested && allowed()
            ? active = RunAsync(label, action) : Task.CompletedTask,
            () => !Busy && !lifetime.IsCancellationRequested && allowed());

    private async Task RunAsync(string label, Func<CancellationToken, Task> action)
    {
        Busy = true;
        Message = label + "…";
        try
        {
            await action(lifetime.Token);
            if (Message == label + "…") Message = label + " completed.";
            Refresh();
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Message = $"{label} failed ({ex.GetType().Name})."; }
        finally { Busy = false; }
    }

    partial void OnHubChanged(HubDto? value)
    {
        Projects = [];
        Project = null;
        LoadProjects.NotifyCanExecuteChanged();
        Enroll.NotifyCanExecuteChanged();
    }
    partial void OnProjectChanged(ProjectDto? value) => Enroll.NotifyCanExecuteChanged();
    partial void OnSelectedRowChanged(ManifestProjectStatus? value)
    {
        ChangeInterval.NotifyCanExecuteChanged();
        Remove.NotifyCanExecuteChanged();
        CheckNow.NotifyCanExecuteChanged();
    }
    partial void OnBusyChanged(bool value)
    {
        foreach (var command in new[] { SignIn, SignOut, LoadHubs, LoadProjects, Enroll, ChangeInterval, Remove, CheckNow })
            command.NotifyCanExecuteChanged();
    }

    private void StatusChanged(object? sender, EventArgs e) => ui.Post(_ => Refresh(), null);

    private void Refresh()
    {
        if (!SignedIn || lifetime.IsCancellationRequested) return;
        var selected = SelectedRow;
        Rows = runner.GetSnapshot();
        SelectedRow = Rows.FirstOrDefault(r => r.HubId == selected?.HubId && r.ProjectId == selected.ProjectId);
    }

    public async Task ShutdownAsync()
    {
        runner.StatusChanged -= StatusChanged;
        lifetime.Cancel();
        await active;
        await runner.StopAsync();
        lifetime.Dispose();
    }
}
