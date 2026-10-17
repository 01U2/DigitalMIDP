using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// ViewModel for managing hub selection.
/// </summary>
public sealed partial class HubSelectionViewModel : ObservableObject
{
    private readonly IFileListingUseCase _fileListingUseCase;
    private readonly ILogger<HubSelectionViewModel> _logger;

    [ObservableProperty]
    private List<HubDto> hubs = [];

    [ObservableProperty]
    private HubDto? selectedHub;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public HubSelectionViewModel(
        IFileListingUseCase fileListingUseCase,
        ILogger<HubSelectionViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(fileListingUseCase);
        ArgumentNullException.ThrowIfNull(logger);

        _fileListingUseCase = fileListingUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Loads available hubs for the authenticated user.
    /// </summary>
    [RelayCommand]
    public async Task LoadHubsAsync(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            _logger.LogInformation("Loading hubs");

            var response = await _fileListingUseCase.GetHubsAsync(accessToken, CancellationToken.None);
            Hubs = new List<HubDto>(response.Hubs);

            if (Hubs.Count > 0)
            {
                SelectedHub = Hubs[0];
                _logger.LogInformation("Loaded {HubCount} hubs", Hubs.Count);
            }
            else
            {
                ErrorMessage = "No hubs found for this account";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load hubs");

            // Create detailed error message with inner exceptions
            var errorDetails = ex.Message;
            if (ex.InnerException != null)
            {
                errorDetails += $"\n\nInner: {ex.InnerException.Message}";
                if (ex.InnerException.InnerException != null)
                {
                    errorDetails += $"\n\nDeeper: {ex.InnerException.InnerException.Message}";
                }
            }

            ErrorMessage = $"Failed to load hubs: {errorDetails}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Clears the selected hub.
    /// </summary>
    [RelayCommand]
    public void ClearSelection()
    {
        SelectedHub = null;
        Hubs.Clear();
        ErrorMessage = null;
    }
}
