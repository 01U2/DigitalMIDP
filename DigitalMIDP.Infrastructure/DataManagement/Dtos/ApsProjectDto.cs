using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// DTO representing an APS Project from the API response.
/// </summary>
internal sealed class ApsProjectDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("attributes")]
    public required ProjectAttributes Attributes { get; init; }

    public sealed class ProjectAttributes
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}

/// <summary>
/// Response wrapper for APS Projects API.
/// </summary>
internal sealed class ApsProjectsResponse
{
    [JsonPropertyName("data")]
    public required IEnumerable<ApsProjectDto> Data { get; init; }

    [JsonPropertyName("links")]
    public PaginationLinks? Links { get; init; }
}
