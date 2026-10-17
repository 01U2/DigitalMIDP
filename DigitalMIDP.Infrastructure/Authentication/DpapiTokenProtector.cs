using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Protects tokens with Windows DPAPI, scoped to the current user.
///
/// <para>
/// The threat this addresses is specific: a crash dump, a swap/hibernation file, or another process
/// reading this one's memory. A refresh token is long-lived and, on its own, enough to mint fresh
/// access tokens for the user's ACC account, so it is the one credential here worth real effort to
/// keep out of plain sight. Held as a .NET <c>string</c> it is immutable, garbage-collected
/// wherever the GC pleases, cannot be reliably zeroed, and shows up in a dump as readable text.
/// </para>
///
/// <para>
/// DPAPI with <see cref="DataProtectionScope.CurrentUser"/> ties the ciphertext to the signed-in
/// Windows user, so the bytes are useless both in a dump and to any other account on the machine.
/// It does not defend against code running as the same user at the same time - that attacker can
/// simply ask DPAPI to decrypt too - which is an inherent limit of any local protection and the
/// reason the plaintext is also kept alive for as short a time as possible.
/// </para>
///
/// <para>
/// An additional entropy value is mixed in so that ciphertext produced here cannot be decrypted by
/// another application running as the same user without also knowing it.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiTokenProtector : ITokenProtector
{
    /// <summary>
    /// Application-specific DPAPI entropy. Not a secret (it ships in the binary) and not trying to
    /// be one - it only scopes the ciphertext to this application, so another process running as
    /// the same user cannot decrypt it by passing the bytes straight back to DPAPI.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DigitalMIDP.ApsTokenStore.v1");

    private readonly ILogger<DpapiTokenProtector> _logger;

    public DpapiTokenProtector(ILogger<DpapiTokenProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public byte[] Protect(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        // The UTF-8 bytes are zeroed as soon as DPAPI has copied them, so the only lasting copy of
        // the plaintext is whatever the caller's own string holds.
        var plaintext = Encoding.UTF8.GetBytes(token);

        try
        {
            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string? Unprotect(byte[] protectedToken)
    {
        ArgumentNullException.ThrowIfNull(protectedToken);

        if (protectedToken.Length == 0)
        {
            return null;
        }

        byte[]? plaintext = null;

        try
        {
            plaintext = ProtectedData.Unprotect(protectedToken, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            // Happens when the stored bytes were produced under a different Windows user or a
            // migrated profile. There is nothing to recover - the caller treats a null as "no token
            // held" and the user signs in again. Never log the bytes themselves.
            _logger.LogWarning(ex, "Stored token could not be decrypted for the current Windows user");
            return null;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }
}

/// <summary>
/// Fallback used when DPAPI is unavailable - it keeps the token as plain UTF-8 bytes and offers no
/// protection at all.
///
/// <para>
/// This exists so the application still runs where <c>ProtectedData</c> throws
/// <see cref="PlatformNotSupportedException"/> rather than failing to sign in. It is never selected
/// on Windows: <c>ServiceCollectionExtensions</c> picks <see cref="DpapiTokenProtector"/> there,
/// and selecting this one is logged as a warning precisely because it is a downgrade.
/// </para>
/// </summary>
internal sealed class UnprotectedTokenProtector : ITokenProtector
{
    public UnprotectedTokenProtector(ILogger<UnprotectedTokenProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogWarning(
            "Token protection is unavailable on this platform ({Platform}) - tokens will be held in memory unencrypted",
            RuntimeInformation.OSDescription);
    }

    public byte[] Protect(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Encoding.UTF8.GetBytes(token);
    }

    public string? Unprotect(byte[] protectedToken)
    {
        ArgumentNullException.ThrowIfNull(protectedToken);
        return protectedToken.Length == 0 ? null : Encoding.UTF8.GetString(protectedToken);
    }
}
