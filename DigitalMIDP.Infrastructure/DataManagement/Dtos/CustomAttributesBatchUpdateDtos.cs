namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// A single attribute id/value pair sent to the batch-update endpoint.
/// POST /bim360/docs/v1/projects/{projectId}/versions/{versionUrn}/custom-attributes:batch-update
/// The endpoint expects a raw JSON array of these objects as the request body.
/// </summary>
public sealed class CustomAttributesBatchUpdateRequestItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Response from the custom-attributes batch-update endpoint.
/// </summary>
public sealed class CustomAttributesBatchUpdateResponseDto
{
    [JsonPropertyName("results")]
    public CustomAttributesBatchUpdateResultItemDto[] Results { get; set; } = [];
}

public sealed class CustomAttributesBatchUpdateResultItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Structured error body ACC returns for a rejected custom-attributes update
/// (e.g. HTTP 400 with code "CUSTOM_ATTRIBUTE_CONSTRAINT_FAIL").
/// </summary>
public sealed class CustomAttributesUpdateErrorResponseDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("errors")]
    public CustomAttributesUpdateErrorItemDto[]? Errors { get; set; }
}

public sealed class CustomAttributesUpdateErrorItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
