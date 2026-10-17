namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Result of attempting to open a file locally or via fallback.
/// </summary>
public record FileOpenResult(
    bool Success,
    string? LocalPath,
    string? ErrorMessage,
    string? FallbackWebUrl);
