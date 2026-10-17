using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of IAuthenticationUseCase.
/// Orchestrates the OAuth 2.0 authentication flow.
/// </summary>
public sealed class AuthenticationUseCase : IAuthenticationUseCase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ITokenStorageService _tokenStorageService;

    public AuthenticationUseCase(
        IAuthenticationService authenticationService,
        ITokenStorageService tokenStorageService)
    {
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(tokenStorageService);

        _authenticationService = authenticationService;
        _tokenStorageService = tokenStorageService;
    }

    /// <summary>
    /// Executes the authentication flow.
    /// </summary>
    public async Task<AuthenticationResponseDto> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var authResult = await _authenticationService.AuthenticateAsync(cancellationToken);

            // Store tokens for automatic injection by ApsAuthorizationHandler
            await _tokenStorageService.SaveTokensAsync(
                authResult.AccessToken,
                authResult.RefreshToken,
                authResult.ExpiresInSeconds);

            return new AuthenticationResponseDto
            {
                AccessToken = authResult.AccessToken,
                RefreshToken = authResult.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(authResult.ExpiresInSeconds)
            };
        }
        catch (Exception ex)
        {
            throw new Exceptions.ApplicationException("Authentication flow failed", ex);
        }
    }
}
