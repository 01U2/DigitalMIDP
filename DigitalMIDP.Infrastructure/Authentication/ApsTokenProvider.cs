using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Domain.ValueObjects;
using DigitalMIDP.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Single implementation of both <see cref="ITokenProvider"/> (the per-request fast path) and
/// <see cref="ITokenStorageService"/> (the sign-in/sign-out lifecycle).
///
/// <para>
/// This replaces the earlier chain of <c>TokenStorageService</c> -&gt; <c>ITokenStorage</c> -&gt;
/// expiry check -&gt; return, which ran in full for every single APS request. Every call in a
/// project load - hundreds of them - went through an async storage round trip, a DPAPI-equivalent
/// read, an expiry comparison and three log statements, to hand back a token that had not changed
/// since the previous request a millisecond earlier.
/// </para>
///
/// <para>
/// Now the hot path is one volatile field read and a timestamp comparison:
/// </para>
/// <code>
/// HttpClient -> ApsAuthorizationHandler -> ITokenProvider.GetAccessTokenAsync() -> cached token
/// </code>
/// <para>
/// Everything else - reading the store, checking expiry, refreshing, re-saving - happens only on a
/// cache miss, and is serialised behind <see cref="_refreshGate"/> so that a burst of concurrent
/// requests arriving just as the token expires performs exactly one refresh rather than one per
/// request.
/// </para>
/// </summary>
internal sealed class ApsTokenProvider : ITokenProvider, ITokenStorageService, IDisposable
{
    /// <summary>
    /// How long before real expiry the cached token is treated as spent.
    ///
    /// <para>
    /// Deliberately larger than the round trip it guards: a token that passes this check must stay
    /// valid long enough for the request it is about to be attached to - including that request's
    /// own retries (see <c>ApsResilienceHandler</c>) - to finish. Five minutes matches
    /// <see cref="AccessToken.IsExpiringSoon"/>, which the sign-in path has always used.
    /// </para>
    /// </summary>
    private static readonly TimeSpan RefreshLeadTime = TimeSpan.FromMinutes(5);

    private readonly ITokenStorage _tokenStorage;
    private readonly IAuthenticationService _authenticationService;

    /// <summary>
    /// Cleared on sign-out. Everything in it was read with the outgoing user's credentials - a
    /// project's folders, its attribute schema, its files' paths - so carrying it into the next
    /// sign-in would show one user data fetched as another.
    /// </summary>
    private readonly IApsMetadataCache _metadataCache;

    private readonly ILogger<ApsTokenProvider> _logger;

    /// <summary>
    /// Serialises refreshes. Only ever contended on a cache miss, so it costs nothing on the hot
    /// path.
    /// </summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>
    /// The cached token and the instant it stops being handed out. Replaced wholesale as a single
    /// reference so a reader can never observe a token paired with the wrong deadline.
    ///
    /// <para>
    /// This is the one place the access token is held as plaintext. It has to be: it is stamped onto
    /// every outgoing request, and re-decrypting it per request is exactly the per-request work this
    /// class exists to remove. The long-lived refresh token is never cached here - it stays
    /// encrypted in <see cref="ProtectedTokenStorage"/> and is only decrypted for the moment a
    /// refresh actually needs it.
    /// </para>
    /// </summary>
    private volatile CachedAccessToken? _cached;

    public ApsTokenProvider(
        ITokenStorage tokenStorage,
        IAuthenticationService authenticationService,
        IApsMetadataCache metadataCache,
        ILogger<ApsTokenProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(tokenStorage);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(metadataCache);
        ArgumentNullException.ThrowIfNull(logger);

        _tokenStorage = tokenStorage;
        _authenticationService = authenticationService;
        _metadataCache = metadataCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // The entire fast path. No await, no storage access, no logging - a request that reuses a
        // valid token must stay this cheap, because every APS call in the application makes it.
        var cached = _cached;
        if (cached is not null && cached.UsableUntilUtc > DateTime.UtcNow)
        {
            return Task.FromResult(cached.Token);
        }

        return GetAccessTokenSlowAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Kept as an alias of <see cref="GetAccessTokenAsync"/> so the two interfaces cannot drift
    /// apart: callers holding either one get the same cache and the same refresh behaviour.
    /// </remarks>
    public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken) =>
        GetAccessTokenAsync(cancellationToken);

    /// <summary>
    /// The cache-miss path: adopt whatever the store holds if it is still good, otherwise refresh.
    /// Separated from <see cref="GetAccessTokenAsync"/> so the fast path has no async state machine
    /// at all.
    /// </summary>
    private async Task<string> GetAccessTokenSlowAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);

        try
        {
            // Re-check inside the gate: if several requests missed the cache together, the first one
            // through has already refreshed and the rest must not refresh again.
            var cached = _cached;
            if (cached is not null && cached.UsableUntilUtc > DateTime.UtcNow)
            {
                return cached.Token;
            }

            var stored = await _tokenStorage.GetAccessTokenAsync();

            if (stored is null)
            {
                _logger.LogWarning("No access token held - the user must sign in");
                throw new InvalidOperationException(
                    "No access token available. User must authenticate first.");
            }

            // Covers the first request after sign-in, where the store was populated directly by
            // SaveTokensAsync and this cache has not been primed yet.
            if (!stored.IsExpiringSoon)
            {
                _cached = CachedAccessToken.From(stored);
                return stored.Token;
            }

            return await RefreshAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Exchanges the stored refresh token for a new pair and primes the cache with it. Called only
    /// from inside <see cref="_refreshGate"/>.
    /// </summary>
    private async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _tokenStorage.GetRefreshTokenAsync();

        if (refreshToken is null)
        {
            _logger.LogWarning("Access token has expired and no refresh token is held");
            throw new InvalidOperationException(
                "Refresh token not available. User must re-authenticate.");
        }

        _logger.LogInformation("Access token is expiring - refreshing");

        var result = await _authenticationService.RefreshAccessTokenAsync(
            refreshToken.Token, cancellationToken);

        ValidateAuthenticationResult(result);

        var newAccessToken = AccessToken.Create(result.AccessToken, result.ExpiresInSeconds);
        var newRefreshToken = RefreshToken.Create(result.RefreshToken);

        await _tokenStorage.SaveTokensAsync(newAccessToken, newRefreshToken);
        _cached = CachedAccessToken.From(newAccessToken);

        _logger.LogInformation(
            "Access token refreshed; next refresh due at {RefreshAt:O}", _cached.UsableUntilUtc);

        return newAccessToken.Token;
    }

    /// <inheritdoc />
    public async Task SaveTokensAsync(string accessToken, string refreshToken, int expiresInSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expiresInSeconds);

        var accessTokenValue = AccessToken.Create(accessToken, expiresInSeconds);

        await _tokenStorage.SaveTokensAsync(accessTokenValue, RefreshToken.Create(refreshToken));

        // Prime the cache from the sign-in itself, so the first APS request after signing in takes
        // the fast path rather than going back to the store to learn what it was just handed.
        _cached = CachedAccessToken.From(accessTokenValue);

        _logger.LogInformation("Tokens saved; access token valid for {Seconds}s", expiresInSeconds);
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentAccessTokenAsync()
    {
        // Prefers the cache but falls back to the store, because logout can happen before this
        // session has made its first APS request.
        var cached = _cached;
        if (cached is not null)
        {
            return cached.Token;
        }

        var stored = await _tokenStorage.GetAccessTokenAsync();
        return stored?.Token;
    }

    /// <inheritdoc />
    public async Task ClearTokensAsync()
    {
        // Cache first: it is the copy every request reads, so dropping it is what actually ends the
        // session. Were the order reversed, a request in flight could still be served a token from
        // the cache after the store had been emptied.
        _cached = null;
        await _tokenStorage.ClearTokensAsync();

        // Project data cached under the outgoing credentials goes with them.
        _metadataCache.Clear();

        _logger.LogInformation("Tokens cleared");
    }

    public void Dispose() => _refreshGate.Dispose();

    /// <summary>
    /// Rejects a token response that deserialised without the parts that make it usable, rather
    /// than caching a null or empty token and failing later with an unexplained 401 on every
    /// subsequent request.
    /// </summary>
    private static void ValidateAuthenticationResult(Application.Dtos.AuthenticationResultDto result)
    {
        if (result is null)
        {
            throw new Application.Exceptions.InvalidTokenException(
                "The token endpoint returned no response body.");
        }

        if (string.IsNullOrWhiteSpace(result.AccessToken))
        {
            throw new Application.Exceptions.InvalidTokenException(
                "The token endpoint returned a response with no access token.");
        }

        if (string.IsNullOrWhiteSpace(result.RefreshToken))
        {
            throw new Application.Exceptions.InvalidTokenException(
                "The token endpoint returned a response with no refresh token, so the session " +
                "could not be renewed again afterwards.");
        }

        if (result.ExpiresInSeconds <= 0)
        {
            throw new Application.Exceptions.InvalidTokenException(
                $"The token endpoint reported a non-positive lifetime ({result.ExpiresInSeconds}s) " +
                "for the new access token.");
        }
    }

    /// <summary>
    /// A token paired with the moment it stops being served from cache.
    /// </summary>
    private sealed record CachedAccessToken(string Token, DateTime UsableUntilUtc)
    {
        public static CachedAccessToken From(AccessToken accessToken)
        {
            // Never past the token's real expiry, even for a token issued with a lifetime shorter
            // than the lead time - such a token is served once and then refreshed, which is correct,
            // rather than being cached beyond the point APS stops accepting it.
            var usableUntil = accessToken.ExpiresAt - RefreshLeadTime;
            return new CachedAccessToken(accessToken.Token, usableUntil);
        }
    }
}
