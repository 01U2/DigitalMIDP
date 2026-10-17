namespace DigitalMIDP.Domain.ValueObjects;

/// <summary>
/// Represents an OAuth 2.0 access token with expiration.
/// </summary>
public sealed record AccessToken
{
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsExpiringSoon => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);

    public static AccessToken Create(string token, int expiresInSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expiresInSeconds);

        return new AccessToken
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expiresInSeconds)
        };
    }
}
