namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Ends the APS session: revokes the tokens at Autodesk, then discards them locally.
///
/// <para>
/// This exists because <c>IAuthenticationService.RevokeAsync</c> was implemented but never called.
/// Logging out only cleared the UI's own state, which left the issued refresh token valid at
/// Autodesk for its full lifetime - so a token captured from the process (or simply still held by a
/// shared machine's session) kept working long after the user believed they had signed out.
/// </para>
/// </summary>
public interface ILogoutUseCase
{
    /// <summary>
    /// Revokes and clears the current session's tokens.
    ///
    /// <para>
    /// Never throws for a revocation that failed. Revocation is a best-effort courtesy to the
    /// server; the part that must always happen is clearing the local tokens, and a network error on
    /// the way out must not leave the application still holding credentials it has told the user it
    /// discarded.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the revocation request.</param>
    /// <returns>
    /// True when Autodesk confirmed the revocation. False when it could not be reached or refused -
    /// the local tokens are cleared either way, so this is for telling the user that the session may
    /// still be live at Autodesk's end, not for deciding whether logout happened.
    /// </returns>
    Task<bool> ExecuteAsync(CancellationToken cancellationToken);
}
