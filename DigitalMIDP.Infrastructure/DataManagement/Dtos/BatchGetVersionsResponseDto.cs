namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// Response from APS batch-get versions endpoint.
/// POST /bim360/docs/v1/projects/{projectId}/versions:batch-get
/// </summary>
public sealed class BatchGetVersionsResponseDto
{
    [JsonPropertyName("results")]
    public BatchGetVersionResultDto[] Results { get; set; } = [];

    [JsonPropertyName("errors")]
    public BatchGetVersionErrorDto[] Errors { get; set; } = [];
}

/// <summary>
/// Individual version/item result from batch-get, includes custom attributes.
/// </summary>
public sealed class BatchGetVersionResultDto
{
    [JsonPropertyName("urn")]
    public string? Urn { get; set; }

    [JsonPropertyName("itemUrn")]
    public string? ItemUrn { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("lastModifiedTime")]
    public string? LastModifiedTime { get; set; }

    [JsonPropertyName("lastModifiedUserName")]
    public string? LastModifiedUserName { get; set; }

    [JsonPropertyName("revisionNumber")]
    public int? RevisionNumber { get; set; }

    [JsonPropertyName("approvalStatus")]
    public ApprovalStatusDto? ApprovalStatus { get; set; }

    [JsonPropertyName("customAttributes")]
    public CustomAttributeDto[]? CustomAttributes { get; set; } = [];
}

public sealed class ApprovalStatusDto
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Error result from batch-get response payload.
/// </summary>
public sealed class BatchGetVersionErrorDto
{
    [JsonPropertyName("Urn")]
    public string? ItemUrn { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Custom attribute id/name/type/value for a file/item. The "id" and "type" here are the same
/// attribute definition id/type used by custom-attribute-definitions and the update endpoint - so
/// any attribute with a real value on a file can be made editable even if it never shows up in the
/// separate definitions listing endpoints (folder-scoped or project-level).
/// </summary>
public sealed class CustomAttributeDto
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
