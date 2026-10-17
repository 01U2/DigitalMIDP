using DigitalMIDP.Domain.ValueObjects;

namespace DigitalMIDP.Domain.Interfaces;

/// <summary>
/// Repository interface for authentication operations.
/// Implemented by Infrastructure layer.
/// </summary>
public interface IAuthenticationRepository
{
    /// <summary>
    /// Initiates the OAuth 2.0 authorization flow with PKCE.
    /// Returns the authorization URL for the system browser.
    /// </summary>
    Task<string> GetAuthorizationUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges the authorization code for access and refresh tokens.
    /// </summary>
    Task<(AccessToken AccessToken, RefreshToken RefreshToken)> ExchangeCodeForTokensAsync(
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the access token using the refresh token.
    /// </summary>
    Task<(AccessToken AccessToken, RefreshToken RefreshToken)> RefreshTokenAsync(
        RefreshToken refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently authenticated user's information.
    /// </summary>
    Task<UserInfo> GetUserInfoAsync(AccessToken accessToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents authenticated user information.
/// </summary>
public sealed record UserInfo
{
    public required string UserId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
}
