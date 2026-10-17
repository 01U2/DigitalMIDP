namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Represents the related custom-attribute schema for a project, excluding filename components.
/// Metadata fields are gathered from project-level and top-folder custom-attribute definitions;
/// they are not limited to fields listed in the project's naming standard.
/// </summary>
public sealed record ProjectNamingStandardDto(
    string Id,
    string Name,
    IReadOnlyList<MetadataFieldDisplayDto> Metadata
);
