using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of <see cref="ILogoutUseCase"/>.
/// </summary>
public sealed class LogoutUseCase : ILogoutUseCase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ITokenStorageService _tokenStorageService;
    private readonly ILogger<LogoutUseCase> _logger;

    public LogoutUseCase(
        IAuthenticationService authenticationService,
        ITokenStorageService tokenStorageService,
        ILogger<LogoutUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(tokenStorageService);
        ArgumentNullException.ThrowIfNull(logger);

        _authenticationService = authenticationService;
        _tokenStorageService = tokenStorageService;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Signing out of APS");

        // Read the token BEFORE clearing, and read it without triggering a refresh: minting a fresh
        // token on the way out would revoke the new one and leave the one actually in use alive.
        var accessToken = await _tokenStorageService.GetCurrentAccessTokenAsync();

        var revoked = false;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogInformation("No access token held - nothing to revoke");
        }
        else
        {
            try
            {
                await _authenticationService.RevokeAsync(accessToken, cancellationToken);
                revoked = true;
                _logger.LogInformation("APS token revoked");
            }
            catch (Exception ex)
            {
                // Swallowed on purpose, and the only place in this codebase where that is the right
                // call: the local tokens MUST still be cleared below. Failing the logout because
                // Autodesk could not be reached would leave the application holding live credentials
                // while telling the user it had signed them out - strictly worse than a token that
                // outlives the session at Autodesk's end, which the return value reports.
                _logger.LogWarning(
                    ex, "Token revocation failed; clearing local tokens anyway. The session may remain valid at Autodesk until the token expires");
            }
        }

        // Unconditional, including when revocation threw and when cancellation was requested.
        await _tokenStorageService.ClearTokensAsync();

        _logger.LogInformation("Sign-out complete (revoked at Autodesk: {Revoked})", revoked);

        return revoked;
    }
}
