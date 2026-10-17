namespace DigitalMIDP.Application.Interfaces;

/// <summary>
/// Service interface for storing and retrieving authentication tokens.
///
/// <para>
/// Per-request token retrieval does NOT go through here - that is <see cref="ITokenProvider"/>,
/// whose whole purpose is to answer from a cached value without touching the store. This interface
/// is for the lifecycle operations: saving what a sign-in produced, reading the current access
/// token so logout can revoke it, and clearing everything.
/// </para>
/// </summary>
public interface ITokenStorageService
{
    /// <summary>
    /// Saves access and refresh tokens.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="expiresInSeconds">Token expiration time in seconds.</param>
    Task SaveTokensAsync(string accessToken, string refreshToken, int expiresInSeconds);

    /// <summary>
    /// Gets a valid access token, refreshing if necessary.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>A valid access token.</returns>
    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the access token currently held, without refreshing it and without throwing when
    /// there is none.
    ///
    /// <para>
    /// This exists for logout: revoking a token requires the exact token the session has been
    /// using, and triggering a refresh on the way out would mint a brand-new token only to
    /// immediately revoke it (and leave the one actually in use alive). An expired token is
    /// returned as-is - revoking it is harmless and the server decides.
    /// </para>
    /// </summary>
    /// <returns>The stored access token, or null when the user is not signed in.</returns>
    Task<string?> GetCurrentAccessTokenAsync();

    /// <summary>
    /// Clears all stored tokens.
    /// </summary>
    Task ClearTokensAsync();
}
