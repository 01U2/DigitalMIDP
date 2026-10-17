using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalMIDP.Services;

namespace DigitalMIDP.ViewModels;

/// <summary>
/// First-run setup: captures the company's Autodesk APS Client ID and persists it to the per-user
/// configuration file.
/// </summary>
public sealed partial class ClientIdSetupViewModel : ObservableObject
{
    private readonly string _targetPath;

    public ClientIdSetupViewModel(string targetPath, string redirectUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        _targetPath = targetPath;
        RedirectUri = redirectUri ?? string.Empty;
    }

    /// <summary>Raised with <c>true</c> after a successful save, <c>false</c> on cancel.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The callback URL the company must register in its APS application.</summary>
    public string RedirectUri { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    private bool CanSave() => ApsClientConfiguration.IsValidClientId(ClientId);

    partial void OnClientIdChanged(string value)
    {
        ErrorMessage = string.IsNullOrWhiteSpace(value) || CanSave()
            ? null
            : "A Client ID is 20-64 letters and digits, as shown in your APS application.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        try
        {
            ApsClientConfiguration.SaveClientId(_targetPath, ClientId);
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = $"Could not save the Client ID: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
