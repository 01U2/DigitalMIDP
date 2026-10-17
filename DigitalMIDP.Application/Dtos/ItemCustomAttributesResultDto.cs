namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Result from batch-fetching custom attribute values for multiple items.
/// Maps item URN to its custom attributes.
/// </summary>
public sealed record ItemCustomAttributesResultDto(
    string Urn,
    string Name,
    string? Title,
    string? LastModifiedTime,
    string? LastModifiedUserName,
    int? RevisionNumber,
    string? ApprovalStatus,
    string? PathInProject,
    string? WebViewLink,
    IReadOnlyDictionary<string, string?> Attributes,
    string? VersionUrn = null
);
