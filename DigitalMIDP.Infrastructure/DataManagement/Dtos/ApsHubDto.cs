using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// DTO representing an APS Hub from the API response.
/// </summary>
internal sealed class ApsHubDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("attributes")]
    public required HubAttributes Attributes { get; init; }

    public sealed class HubAttributes
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("region")]
        public string? Region { get; init; }
    }
}

/// <summary>
/// Response wrapper for APS Hubs API.
/// </summary>
internal sealed class ApsHubsResponse
{
    [JsonPropertyName("data")]
    public required IEnumerable<ApsHubDto> Data { get; init; }

    [JsonPropertyName("links")]
    public PaginationLinks? Links { get; init; }
}
