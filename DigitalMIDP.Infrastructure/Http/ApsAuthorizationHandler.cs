using System.Net.Http.Headers;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Http;

/// <summary>
/// HTTP message handler that stamps the APS bearer token onto outgoing requests.
///
/// <para>
/// Deliberately thin. Every APS call in the application passes through here, so the common case -
/// a request that reuses the token the previous request used - must do as little as possible:
/// one call into <see cref="ITokenProvider"/>, which answers from a cached value, and one header
/// assignment. No logging on success, because at Debug level a single project load produced
/// hundreds of "retrieving token"/"header added" pairs that said nothing and buried everything
/// else. Expiry checking and refreshing live behind the provider and only run when the cache is
/// actually cold.
/// </para>
/// </summary>
internal sealed class ApsAuthorizationHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly ILogger<ApsAuthorizationHandler> _logger;

    public ApsAuthorizationHandler(
        ITokenProvider tokenProvider,
        ILogger<ApsAuthorizationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Pre-signed S3 transfer URLs are not autodesk.com hosts and must NOT carry a bearer token -
        // S3 rejects a signed request that also presents one. They use a separate unauthenticated
        // client anyway (see DataManagementService.TransferHttpClientName); this check is the second
        // line of defence.
        if (request.RequestUri?.Host.Contains("autodesk.com", StringComparison.OrdinalIgnoreCase) != true)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        string token;

        try
        {
            token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Not signed in, or the refresh token is gone. Logged once here because it is the point
            // where an otherwise valid-looking request is abandoned, and rethrown so the request is
            // never sent unauthenticated - an anonymous APS call returns a 401 that reads like a
            // permissions problem rather than "you are signed out".
            _logger.LogError(
                ex, "No valid token for APS request {Method} {Path}; the user must sign in",
                request.Method, request.RequestUri.AbsolutePath);
            throw;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError(
                "Token provider returned an empty access token for {Method} {Path}",
                request.Method, request.RequestUri.AbsolutePath);

            throw new InvalidOperationException(
                "Access token is not available. User must authenticate first.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
