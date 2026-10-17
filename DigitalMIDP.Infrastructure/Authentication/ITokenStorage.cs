using DigitalMIDP.Domain.ValueObjects;

namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Interface for secure token storage and retrieval.
/// </summary>
public interface ITokenStorage
{
    /// <summary>
    /// Stores authentication tokens securely.
    /// </summary>
    /// <param name="accessToken">The access token to store.</param>
    /// <param name="refreshToken">The refresh token to store.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveTokensAsync(AccessToken accessToken, RefreshToken refreshToken);

    /// <summary>
    /// Retrieves stored access token.
    /// </summary>
    /// <returns>The stored access token, or null if not found.</returns>
    Task<AccessToken?> GetAccessTokenAsync();

    /// <summary>
    /// Retrieves stored refresh token.
    /// </summary>
    /// <returns>The stored refresh token, or null if not found.</returns>
    Task<RefreshToken?> GetRefreshTokenAsync();

    /// <summary>
    /// Clears all stored tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearTokensAsync();
}
