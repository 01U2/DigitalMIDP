namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Encrypts and decrypts a token so it is not held in the process as readable text.
///
/// <para>
/// Abstracted rather than calling DPAPI inline so the token store stays unit-testable off a real
/// Windows user profile, and so the protection mechanism can be swapped (e.g. for a future
/// Credential Manager-backed store) without touching the store's own logic.
/// </para>
/// </summary>
internal interface ITokenProtector
{
    /// <summary>Encrypts a token, returning opaque bytes safe to keep in a field.</summary>
    byte[] Protect(string token);

    /// <summary>
    /// Decrypts bytes produced by <see cref="Protect"/>.
    /// </summary>
    /// <returns>The token, or null when the bytes cannot be decrypted (e.g. a different user).</returns>
    string? Unprotect(byte[] protectedToken);
}
