namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Represents the display value of a metadata field for a selected item.
/// Contains both the field definition and the actual value for display in the UI.
/// </summary>
public sealed record MetadataFieldValueDto(
    string FieldName,
    string FieldType,
    string? DisplayValue,
    string? FieldDescription
);
