using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Manages authentication state, child ViewModels, and orchestrates the application flow.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAuthenticationUseCase _authenticationUseCase;
    private readonly ILogoutUseCase _logoutUseCase;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    [ObservableProperty]
    private bool isAuthenticated;

    [ObservableProperty]
    private bool isAuthenticating;

    [ObservableProperty]
    private string? errorMessage;

    // No access/refresh token properties here on purpose: tokens live only in the DPAPI-protected
    // token store and are attached to requests by ApsAuthorizationHandler. Only the expiry is kept,
    // for display/diagnostics.
    [ObservableProperty]
    private DateTime? tokenExpiresAt;

    /// <summary>
    /// Child ViewModel for Hub selection.
    /// </summary>
    public HubSelectionViewModel HubSelectionViewModel { get; }

    /// <summary>
    /// Child ViewModel for Project selection.
    /// </summary>
    public ProjectSelectionViewModel ProjectSelectionViewModel { get; }

    /// <summary>
    /// Child ViewModel for file/folder browsing.
    /// </summary>
    public FilesViewModel FilesViewModel { get; }

    public MainViewModel(
        IAuthenticationUseCase authenticationUseCase,
        ILogoutUseCase logoutUseCase,
        HubSelectionViewModel hubSelectionViewModel,
        ProjectSelectionViewModel projectSelectionViewModel,
        FilesViewModel filesViewModel,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(authenticationUseCase);
        ArgumentNullException.ThrowIfNull(logoutUseCase);
        ArgumentNullException.ThrowIfNull(hubSelectionViewModel);
        ArgumentNullException.ThrowIfNull(projectSelectionViewModel);
        ArgumentNullException.ThrowIfNull(filesViewModel);
        ArgumentNullException.ThrowIfNull(logger);

        _authenticationUseCase = authenticationUseCase;
        _logoutUseCase = logoutUseCase;
        HubSelectionViewModel = hubSelectionViewModel;
        ProjectSelectionViewModel = projectSelectionViewModel;
        FilesViewModel = filesViewModel;
        _logger = logger;

        IsAuthenticated = false;
        IsAuthenticating = false;

        // Wire up property change notifications for coordinated updates
        HubSelectionViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HubSelectionViewModel.SelectedHub))
            {
                OnHubSelected();
            }
        };

        ProjectSelectionViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProjectSelectionViewModel.SelectedProject))
            {
                OnProjectSelected();
            }
        };
    }

    /// <summary>
    /// Command to initiate authentication.
    /// </summary>
    [RelayCommand]
    public async Task AuthenticateAsync()
    {
        if (IsAuthenticating)
        {
            return;
        }

        IsAuthenticating = true;
        ErrorMessage = null;

        // Replaced rather than reused, and the previous one disposed - a second sign-in attempt
        // after the first was cancelled must not inherit an already-cancelled token.
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _logger.LogInformation("Starting authentication flow");

            var result = await _authenticationUseCase.ExecuteAsync(_cancellationTokenSource.Token);

            TokenExpiresAt = result.ExpiresAt;
            IsAuthenticated = true;

            _logger.LogInformation("Authentication successful");

            // Automatically load hubs after successful authentication
            try
            {
                await LoadHubsAsync();
            }
            catch (Exception hubEx)
            {
                _logger.LogError(hubEx, "Failed to load hubs after authentication");
                ErrorMessage = $"Authentication succeeded but failed to load hubs: {hubEx.Message}";
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Authentication cancelled by user");
            ErrorMessage = "Authentication was cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed");
            ErrorMessage = $"Authentication failed: {ex.Message}";
        }
        finally
        {
            IsAuthenticating = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Cancels an authentication attempt that is still waiting for the browser callback.
    ///
    /// <para>
    /// Sign-in blocks on a local HTTP listener until the user completes the Autodesk flow (see
    /// <c>OAuthCallbackListener</c>). If they close the browser instead, nothing ever arrives and the
    /// button stays stuck on "Authenticating..." - the token was created but there was no way to
    /// trigger it.
    /// </para>
    /// </summary>
    [RelayCommand]
    public void CancelAuthentication()
    {
        if (!IsAuthenticating)
        {
            return;
        }

        _logger.LogInformation("Authentication cancelled by the user");
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// Command to log out: revokes the session at Autodesk, then clears all local state.
    ///
    /// <para>
    /// This used to be synchronous and purely local - it reset the UI's properties and nothing else.
    /// The tokens stayed in the token store, and the refresh token stayed valid at Autodesk for its
    /// full lifetime, so "Logout" left a usable credential behind on the machine. It now goes
    /// through <see cref="ILogoutUseCase"/>, which revokes first and clears unconditionally.
    /// </para>
    ///
    /// <para>
    /// The generated command keeps the name <c>LogoutCommand</c> (the toolkit strips the <c>Async</c>
    /// suffix), so the existing XAML binding is unchanged.
    /// </para>
    /// </summary>
    [RelayCommand]
    public async Task LogoutAsync()
    {
        _logger.LogInformation("Logging out");

        // The UI is reset first, before awaiting the network call. Logging out should feel immediate
        // and must not appear to fail: revocation is best-effort, and the local session is over
        // either way.
        IsAuthenticated = false;
        TokenExpiresAt = null;
        ErrorMessage = null;

        // Clear child ViewModels state. FilesViewModel also stops its timers and abandons any load
        // still in flight - without that, a five-minute auto-refresh tick would keep firing against
        // a session that no longer has a token.
        HubSelectionViewModel.ClearSelection();
        ProjectSelectionViewModel.ClearSelection();
        FilesViewModel.ResetForSignOut();

        var revoked = await _logoutUseCase.ExecuteAsync(CancellationToken.None);

        if (!revoked)
        {
            // Not an error dialog: the user is signed out of this application regardless. It is said
            // plainly because the honest position is that the token may still be live at Autodesk.
            _logger.LogWarning("Signed out locally, but the token could not be revoked at Autodesk");
        }
    }

    /// <summary>
    /// Command to load hubs for the authenticated user.
    /// </summary>
    [RelayCommand]
    public async Task LoadHubsAsync()
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("Cannot load hubs: not signed in");
            return;
        }

        await HubSelectionViewModel.LoadHubsAsync();
    }

    /// <summary>
    /// Command to load projects for the selected hub.
    /// </summary>
    [RelayCommand]
    public async Task LoadProjectsAsync()
    {
        if (!IsAuthenticated || HubSelectionViewModel.SelectedHub == null)
        {
            _logger.LogWarning("Cannot load projects: not signed in or no hub selected");
            return;
        }

        ProjectSelectionViewModel.CurrentHubId = HubSelectionViewModel.SelectedHub.Id;

        await ProjectSelectionViewModel.LoadProjectsAsync();
    }

    /// <summary>
    /// Handles hub selection changes.
    /// </summary>
    private void OnHubSelected()
    {
        if (HubSelectionViewModel.SelectedHub == null)
        {
            return;
        }

        _logger.LogInformation("Hub selected: {HubName}", HubSelectionViewModel.SelectedHub.Name);

        // Clear project selection when hub changes
        ProjectSelectionViewModel.ClearSelection();
        FilesViewModel.Items.Clear();

        // Automatically load projects for the selected hub
        _ = LoadProjectsAsync();
    }

    /// <summary>
    /// Handles project selection changes.
    /// Loads project context and metadata schema when a project is selected.
    /// </summary>
    private void OnProjectSelected()
    {
        if (ProjectSelectionViewModel.SelectedProject == null || !IsAuthenticated)
        {
            return;
        }

        _logger.LogInformation("Project selected: {ProjectName}", ProjectSelectionViewModel.SelectedProject.Name);

        // Update FilesViewModel with project context (including hubId for metadata loading)
        FilesViewModel.CurrentHubId = HubSelectionViewModel.SelectedHub?.Id;
        FilesViewModel.CurrentHubName = HubSelectionViewModel.SelectedHub?.Name;
        FilesViewModel.CurrentProjectId = ProjectSelectionViewModel.SelectedProject.Id;
        FilesViewModel.CurrentProjectName = ProjectSelectionViewModel.SelectedProject.Name;
        FilesViewModel.CurrentFolderId = null; // null = root folder

        // The folder load fetches the project's metadata with its cancellation token; do not
        // launch a second independent schema request that can complete after a project switch.
        _ = FilesViewModel.LoadFolderItemsAsync();
        _ = FilesViewModel.RefreshSyncStatusAsync();
    }

    /// <summary>
    /// Releases the sign-in cancellation source and disposes the child ViewModel that owns timers.
    ///
    /// <para>
    /// Called from <c>App.OnExit</c>. WPF does not dispose a window's DataContext, and the DI
    /// container holds these ViewModels for the application's lifetime, so nothing else would ever
    /// stop <see cref="FilesViewModel"/>'s dispatcher timers.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        FilesViewModel.Dispose();
    }

    /// <summary>
    /// Checks if the current access token is still valid.
    /// </summary>
    public bool IsTokenValid => TokenExpiresAt.HasValue && DateTime.UtcNow < TokenExpiresAt;

    /// <summary>
    /// Checks if the token is expiring soon (within 5 minutes).
    /// </summary>
    public bool IsTokenExpiringSoon =>
        TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value.AddMinutes(-5);
}