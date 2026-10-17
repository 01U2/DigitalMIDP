using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// Common pagination links returned by APS list endpoints.
/// </summary>
internal sealed class PaginationLinks
{
    [JsonPropertyName("next")]
    public PaginationLink? Next { get; init; }
}

internal sealed class PaginationLink
{
    [JsonPropertyName("href")]
    public string? Href { get; init; }
}
