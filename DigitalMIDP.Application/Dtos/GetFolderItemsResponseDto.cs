namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Response DTO for the get folder items use case.
/// </summary>
public sealed record GetFolderItemsResponseDto
{
    public required IEnumerable<ItemDto> Items { get; init; }
}
