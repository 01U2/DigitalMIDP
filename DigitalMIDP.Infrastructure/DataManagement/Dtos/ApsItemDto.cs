using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// DTO representing an APS Item (folder or file) from the API response.
/// </summary>
internal sealed class ApsItemDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("attributes")]
    public required ItemAttributes Attributes { get; init; }

    [JsonPropertyName("relationships")]
    public ItemRelationships? Relationships { get; init; }

    public sealed class ItemAttributes
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("parentId")]
        public string? ParentId { get; init; }

        [JsonPropertyName("createTime")]
        public DateTime? CreateTime { get; init; }

        [JsonPropertyName("lastModifiedTime")]
        public DateTime? LastModifiedTime { get; init; }

        [JsonPropertyName("fileType")]
        public string? FileType { get; init; }

        [JsonPropertyName("storageSize")]
        public long? StorageSize { get; init; }
    }

    public sealed class ItemRelationships
    {
        [JsonPropertyName("tip")]
        public ItemRelationshipData? Tip { get; init; }
    }

    public sealed class ItemRelationshipData
    {
        [JsonPropertyName("data")]
        public ItemRelationshipResource? Data { get; init; }
    }

    public sealed class ItemRelationshipResource
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }
}
