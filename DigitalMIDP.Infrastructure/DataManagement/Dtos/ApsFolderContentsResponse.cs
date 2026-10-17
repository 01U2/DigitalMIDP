using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// Response wrapper for APS folder contents API.
/// </summary>
internal sealed class ApsFolderContentsResponse
{
    [JsonPropertyName("data")]
    public required IEnumerable<ApsItemDto> Data { get; init; }

    [JsonPropertyName("links")]
    public PaginationLinks? Links { get; init; }
}
