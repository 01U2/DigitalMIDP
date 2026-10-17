namespace DigitalMIDP.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for Autodesk Platform Services (APS) OAuth.
/// </summary>
public sealed class ApsAuthenticationSettings
{
    public required string ClientId { get; init; }
    public required string RedirectUri { get; init; }
    public required string Scopes { get; init; }
    public required string AuthorizeEndpoint { get; init; }
    public required string TokenEndpoint { get; init; }
    public required string UserInfoEndpoint { get; init; }
}
