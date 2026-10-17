using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Use case interface for handling OAuth 2.0 authentication with Autodesk Platform Services (APS).
/// </summary>
public interface IAuthenticationUseCase
{
    /// <summary>
    /// Executes the authentication flow, initiating OAuth 2.0 with PKCE.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Authentication response containing tokens and user information.</returns>
    Task<AuthenticationResponseDto> ExecuteAsync(CancellationToken cancellationToken);
}
