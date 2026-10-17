namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Response DTO for the authentication use case.
/// Contains tokens and expiration information.
/// </summary>
public sealed record AuthenticationResponseDto
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
