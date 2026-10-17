namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Data Transfer Object containing the result of OAuth 2.0 authentication.
/// </summary>
public sealed record AuthenticationResultDto
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required int ExpiresInSeconds { get; init; }
}
