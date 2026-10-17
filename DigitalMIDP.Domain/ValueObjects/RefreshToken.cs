namespace DigitalMIDP.Domain.ValueObjects;

/// <summary>
/// Represents an OAuth 2.0 refresh token.
/// </summary>
public sealed record RefreshToken
{
    public required string Token { get; init; }

    public static RefreshToken Create(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return new RefreshToken { Token = token };
    }
}
