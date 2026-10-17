namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Represents a metadata field from a project's naming standard.
/// Used for displaying custom attributes in the UI.
/// </summary>
public sealed record MetadataFieldDisplayDto(
    string Name,
    string Type,
    string? Description,
    bool Optional,
    IReadOnlyList<MetadataOptionDisplayDto> Options,
    string? DefaultValue,
    int AttributeId = 0
);

/// <summary>
/// Represents a single option value for an ARRAY type metadata field.
/// </summary>
public sealed record MetadataOptionDisplayDto(
    string? Value,
    string? Description
);
