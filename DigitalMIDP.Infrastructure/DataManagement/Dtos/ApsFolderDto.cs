using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// DTO representing an APS Folder from the API response.
/// </summary>
internal sealed class ApsFolderDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("attributes")]
    public required FolderAttributes Attributes { get; init; }

    public sealed class FolderAttributes
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("parentId")]
        public string? ParentId { get; init; }
    }
}
