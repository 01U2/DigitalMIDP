using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// ViewModel for managing project selection within a hub.
/// </summary>
public sealed partial class ProjectSelectionViewModel : ObservableObject
{
    private readonly IFileListingUseCase _fileListingUseCase;
    private readonly ITokenProvider _tokenProvider;
    private readonly ILogger<ProjectSelectionViewModel> _logger;
    private int _generation;

    [ObservableProperty]
    private List<ProjectDto> projects = [];

    [ObservableProperty]
    private ProjectDto? selectedProject;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? currentHubId;

    public ProjectSelectionViewModel(
        IFileListingUseCase fileListingUseCase,
        ITokenProvider tokenProvider,
        ILogger<ProjectSelectionViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(fileListingUseCase);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _fileListingUseCase = fileListingUseCase;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    /// <summary>
    /// Loads projects for the selected hub.
    /// Call this after setting CurrentHubId.
    /// </summary>
    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentHubId))
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        var generation = ++_generation;
        var hubId = CurrentHubId;

        try
        {
            _logger.LogInformation("Loading projects for hub {HubId}", CurrentHubId);

            // Fetched per call, never stored on the ViewModel - see TokenExposureTests.
            var accessToken = await _tokenProvider.GetAccessTokenAsync(CancellationToken.None);
            if (generation != _generation) return;
            var response = await _fileListingUseCase.GetProjectsAsync(hubId!, accessToken, CancellationToken.None);
            if (generation != _generation) return;
            Projects = new List<ProjectDto>(response.Projects);

            _logger.LogInformation("Loaded {ProjectCount} projects", Projects.Count);
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            _logger.LogError(ex, "Failed to load projects for hub {HubId}", CurrentHubId);
            ErrorMessage = $"Failed to load projects: {ex.Message}";
        }
        finally
        {
            if (generation == _generation) IsLoading = false;
        }
    }

    /// <summary>
    /// Clears the selected project.
    /// </summary>
    [RelayCommand]
    public void ClearSelection()
    {
        ++_generation;
        IsLoading = false;
        CurrentHubId = null;
        SelectedProject = null;
        Projects = [];
        ErrorMessage = null;
    }
}
