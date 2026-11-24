namespace DigitalMIDP.Application.Dtos;

public sealed record AttributeEditTarget(ItemDto Item, IReadOnlyList<MetadataFieldDisplayDto> Definitions,
    IReadOnlyDictionary<int, string?> Values);
