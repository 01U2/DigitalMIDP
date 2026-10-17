using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// ViewModel for managing project selection within a hub.
/// </summary>
public sealed partial class ProjectSelectionViewModel : ObservableObject
{
    private readonly IFileListingUseCase _fileListingUseCase;
    private readonly ILogger<ProjectSelectionViewModel> _logger;

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

    [ObservableProperty]
    private string? currentAccessToken;

    public ProjectSelectionViewModel(
        IFileListingUseCase fileListingUseCase,
        ILogger<ProjectSelectionViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(fileListingUseCase);
        ArgumentNullException.ThrowIfNull(logger);

        _fileListingUseCase = fileListingUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Loads projects for the selected hub.
    /// Call this after setting CurrentHubId and CurrentAccessToken.
    /// </summary>
    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentHubId) || string.IsNullOrWhiteSpace(CurrentAccessToken))
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            _logger.LogInformation("Loading projects for hub {HubId}", CurrentHubId);

            var response = await _fileListingUseCase.GetProjectsAsync(CurrentHubId!, CurrentAccessToken!, CancellationToken.None);
            Projects = new List<ProjectDto>(response.Projects);

            _logger.LogInformation("Loaded {ProjectCount} projects", Projects.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load projects for hub {HubId}", CurrentHubId);
            ErrorMessage = $"Failed to load projects: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Clears the selected project.
    /// </summary>
    [RelayCommand]
    public void ClearSelection()
    {
        SelectedProject = null;
        Projects.Clear();
        ErrorMessage = null;
    }
}
