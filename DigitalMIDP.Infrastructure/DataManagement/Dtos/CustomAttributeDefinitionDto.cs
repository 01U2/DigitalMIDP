namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// Shape returned by GET /bim360/docs/v1/projects/{projectId}/folders/{folderId}/custom-attribute-definitions.
/// Distinct from <see cref="MetadataFieldDto"/> (the naming-standards shape): this endpoint uses "id" instead
/// of "attributeId", and represents dropdown options as a flat "arrayValues" string array instead of "options".
/// </summary>
public sealed class CustomAttributeDefinitionDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("optional")]
    public bool? Optional { get; set; }

    /// <summary>
    /// Defensive fallback for a response that reports this the other way round, as "required" rather
    /// than "optional" - the two ACC schema endpoints in this codebase have repeatedly used different
    /// names for the same concept (see the class doc-comments on this DTO and on
    /// NamingStandardComponentDto), so this is read and inverted wherever "optional" itself is absent.
    /// </summary>
    [JsonPropertyName("required")]
    public bool? Required { get; set; }

    [JsonPropertyName("arrayValues")]
    public string[]? ArrayValues { get; set; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
}
