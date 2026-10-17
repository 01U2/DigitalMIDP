namespace DigitalMIDP.Application.Interfaces;

/// <summary>
/// Supplies the bearer token for an outgoing APS request.
///
/// <para>
/// This is deliberately the narrowest possible contract - one method, returning one string - so the
/// per-request path through <c>ApsAuthorizationHandler</c> is a single call into a cached value:
/// </para>
/// <code>
/// HttpClient -> ApsAuthorizationHandler -> ITokenProvider.GetAccessTokenAsync() -> cached token
/// </code>
/// <para>
/// Expiry checking, refreshing, and reading the token store all live behind that call and only run
/// when the cached token is actually missing or close to expiring. A request that simply reuses a
/// valid token does no storage round trip and writes no log entry, which matters because every APS
/// call in the application goes through here - during a project load that is hundreds of calls.
/// </para>
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Returns a usable access token, refreshing it first if the cached one has run out.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token, observed only when a refresh is needed.</param>
    /// <returns>The access token to send as a bearer credential.</returns>
    /// <exception cref="InvalidOperationException">
    /// No token is held and none can be obtained without the user signing in again.
    /// </exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
