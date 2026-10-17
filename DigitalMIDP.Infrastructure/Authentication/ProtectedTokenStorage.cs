using System.Security.Cryptography;
using DigitalMIDP.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Holds the session's tokens for the lifetime of the process, encrypted at rest via
/// <see cref="ITokenProtector"/>.
///
/// <para>
/// Replaces the earlier plain-field store. Nothing is written to disk: the user signs in once per
/// run of the application, and persisting a refresh token would make an attacker's job strictly
/// easier (a file survives the process; the process does not) for the sake of saving one browser
/// round trip per launch. What this class does fix is the in-memory exposure - the refresh token is
/// never held as a <see cref="string"/> between uses, so it does not appear as readable text in a
/// crash dump, page file, or hibernation image.
/// </para>
///
/// <para>
/// The access token is protected the same way for consistency, but it is honestly a weaker win: the
/// token provider caches it in plaintext because it has to be stamped onto every outgoing request
/// (see <c>ApsTokenProvider</c>). Its blast radius is bounded by its one-hour lifetime, whereas the
/// refresh token's is not, which is why the two are treated as different problems.
/// </para>
/// </summary>
internal sealed class ProtectedTokenStorage : ITokenStorage, IDisposable
{
    private readonly ITokenProtector _protector;
    private readonly ILogger<ProtectedTokenStorage> _logger;

    /// <summary>
    /// Guards the three fields below as a set. They are written together and must be read
    /// consistently - an interleaved save would otherwise let a caller pair a new access token with
    /// the previous refresh token.
    /// </summary>
    private readonly object _gate = new();

    private byte[]? _protectedAccessToken;
    private byte[]? _protectedRefreshToken;
    private DateTime _accessTokenExpiresAtUtc;

    public ProtectedTokenStorage(ITokenProtector protector, ILogger<ProtectedTokenStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(logger);

        _protector = protector;
        _logger = logger;
    }

    public Task SaveTokensAsync(AccessToken accessToken, RefreshToken refreshToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(refreshToken);

        var protectedAccess = _protector.Protect(accessToken.Token);
        var protectedRefresh = _protector.Protect(refreshToken.Token);

        lock (_gate)
        {
            ZeroAndRelease(ref _protectedAccessToken);
            ZeroAndRelease(ref _protectedRefreshToken);

            _protectedAccessToken = protectedAccess;
            _protectedRefreshToken = protectedRefresh;
            _accessTokenExpiresAtUtc = accessToken.ExpiresAt;
        }

        // Never log the tokens themselves - only that a pair was stored and when it runs out.
        _logger.LogInformation(
            "Stored protected APS tokens; access token expires at {ExpiresAt:O}", accessToken.ExpiresAt);

        return Task.CompletedTask;
    }

    public Task<AccessToken?> GetAccessTokenAsync()
    {
        byte[]? protectedAccess;
        DateTime expiresAt;

        lock (_gate)
        {
            protectedAccess = _protectedAccessToken;
            expiresAt = _accessTokenExpiresAtUtc;
        }

        if (protectedAccess is null)
        {
            return Task.FromResult<AccessToken?>(null);
        }

        var token = _protector.Unprotect(protectedAccess);

        return Task.FromResult(string.IsNullOrEmpty(token)
            ? null
            : new AccessToken { Token = token, ExpiresAt = expiresAt });
    }

    public Task<RefreshToken?> GetRefreshTokenAsync()
    {
        byte[]? protectedRefresh;

        lock (_gate)
        {
            protectedRefresh = _protectedRefreshToken;
        }

        if (protectedRefresh is null)
        {
            return Task.FromResult<RefreshToken?>(null);
        }

        var token = _protector.Unprotect(protectedRefresh);

        return Task.FromResult(string.IsNullOrEmpty(token)
            ? null
            : RefreshToken.Create(token));
    }

    public Task ClearTokensAsync()
    {
        Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shared by <see cref="ClearTokensAsync"/> and <see cref="Dispose"/>. Clearing is entirely
    /// synchronous, so Dispose calls this directly rather than blocking on the Task-returning
    /// overload - the codebase does not block on async work anywhere.
    /// </summary>
    private void Clear()
    {
        lock (_gate)
        {
            ZeroAndRelease(ref _protectedAccessToken);
            ZeroAndRelease(ref _protectedRefreshToken);
            _accessTokenExpiresAtUtc = default;
        }

        _logger.LogInformation("Cleared stored APS tokens");
    }

    public void Dispose() => Clear();

    /// <summary>
    /// Overwrites the ciphertext before dropping the reference, so a cleared token does not linger
    /// in a heap block until the GC happens to reuse it.
    /// </summary>
    private static void ZeroAndRelease(ref byte[]? buffer)
    {
        if (buffer is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(buffer);
        buffer = null;
    }
}
