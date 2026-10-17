using System.Text;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Exceptions;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Infrastructure.Configuration;
using DigitalMIDP.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Infrastructure.Authentication;

/// <summary>
/// Implementation of IAuthenticationService for Autodesk Platform Services (APS).
/// Implements 3-legged OAuth 2.0 flow with PKCE.
/// </summary>
internal sealed class AuthenticationService : IAuthenticationService
{
    private readonly ApsAuthenticationSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public AuthenticationService(
        ApsAuthenticationSettings settings,
        HttpClient httpClient,
        ILogger<AuthenticationService> logger,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _settings = settings;
        _httpClient = httpClient;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Initiates the OAuth 2.0 3-legged flow with PKCE.
    /// </summary>
    public async Task<AuthenticationResultDto> AuthenticateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating OAuth 2.0 3-legged authentication flow with PKCE");

        // Generate PKCE parameters
        var codeVerifier = PkceHelper.GenerateCodeVerifier();
        var codeChallenge = PkceHelper.GenerateCodeChallenge(codeVerifier);

        // Build authorization URL
        var authUrl = BuildAuthorizationUrl(codeChallenge);
        _logger.LogDebug("Authorization URL: {AuthUrl}", authUrl);

        // Start local HTTP listener for callback
        using var callbackListener = new OAuthCallbackListener(
            _settings.RedirectUri,
            _loggerFactory.CreateLogger<OAuthCallbackListener>());

        // Start listening in background (before opening browser)
        var callbackTask = callbackListener.ListenForCallbackAsync(cancellationToken);

        // Open system browser for user to authenticate
        OpenSystemBrowser(authUrl);

        // Wait for callback with authorization code
        var authorizationCode = await callbackTask;

        // Exchange authorization code for tokens
        return await ExchangeCodeForTokensAsync(authorizationCode, codeVerifier, cancellationToken);
    }

    /// <summary>
    /// Refreshes an expired access token.
    /// </summary>
    public async Task<AuthenticationResultDto> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        _logger.LogInformation("Refreshing access token");

        try
        {
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "client_id", _settings.ClientId },
                { "refresh_token", refreshToken }
            });

            var response = await _httpClient.PostAsync(_settings.TokenEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsAsync<TokenResponseDto>(cancellationToken);
            _logger.LogInformation("Access token refreshed successfully");

            return new AuthenticationResultDto
            {
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken ?? refreshToken,
                ExpiresInSeconds = result.ExpiresIn
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh access token");
            throw new InvalidTokenException("Unable to refresh access token", ex);
        }
    }

    /// <summary>
    /// Revokes the current authentication tokens.
    /// </summary>
    public async Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        _logger.LogInformation("Revoking access token");

        try
        {
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "token", accessToken }
            });

            var response = await _httpClient.PostAsync(
                $"{_settings.TokenEndpoint}/revoke",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Access token revoked successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke access token");
            throw new Application.Exceptions.ApplicationException("Unable to revoke access token", ex);
        }
    }

    private string BuildAuthorizationUrl(string codeChallenge)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "client_id", _settings.ClientId },
            { "response_type", "code" },
            { "redirect_uri", _settings.RedirectUri },
            { "scope", _settings.Scopes },
            { "code_challenge", codeChallenge },
            { "code_challenge_method", "S256" }
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"{_settings.AuthorizeEndpoint}?{queryString}";
    }

    private void OpenSystemBrowser(string url)
    {
        try
        {
            // Platform-specific browser launch
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Linux))
            {
                System.Diagnostics.Process.Start("xdg-open", url);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
            {
                System.Diagnostics.Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open system browser");
            throw new Application.Exceptions.ApplicationException($"Unable to open browser for authentication: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Exchanges the authorization code for access and refresh tokens.
    /// </summary>
    private async Task<AuthenticationResultDto> ExchangeCodeForTokensAsync(
        string authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        _logger.LogInformation("Exchanging authorization code for access token");

        try
        {
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "client_id", _settings.ClientId },
                { "code", authorizationCode },
                { "redirect_uri", _settings.RedirectUri },
                { "code_verifier", codeVerifier }
            });

            var response = await _httpClient.PostAsync(_settings.TokenEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsAsync<TokenResponseDto>(cancellationToken);
            _logger.LogInformation("Access token obtained successfully; expires in {ExpiresIn} seconds", result.ExpiresIn);

            return new AuthenticationResultDto
            {
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken ?? string.Empty,
                ExpiresInSeconds = result.ExpiresIn
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange authorization code for tokens");
            throw new InvalidTokenException("Unable to obtain access token", ex);
        }
    }
}

/// <summary>
/// Internal DTO for APS token endpoint response.
/// </summary>
internal sealed class TokenResponseDto
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";
}
