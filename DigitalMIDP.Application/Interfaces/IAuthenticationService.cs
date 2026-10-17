using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.Interfaces;

/// <summary>
/// Service interface for OAuth 2.0 authentication with Autodesk Platform Services (APS).
/// Implements 3-legged OAuth with PKCE for desktop applications.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Initiates the 3-legged OAuth flow with PKCE.
    /// Opens the system browser for user authentication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Authentication result containing tokens and expiration information.</returns>
    Task<AuthenticationResultDto> AuthenticateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes an expired or expiring access token.
    /// </summary>
    /// <param name="refreshToken">The refresh token to use for obtaining a new access token.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>New authentication result with refreshed access token.</returns>
    Task<AuthenticationResultDto> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the current authentication tokens.
    /// </summary>
    /// <param name="accessToken">The access token to revoke.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    Task RevokeAsync(string accessToken, CancellationToken cancellationToken);
}
