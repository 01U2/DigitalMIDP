namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Response DTO for the get hubs use case.
/// </summary>
public sealed record GetHubsResponseDto
{
    public required IEnumerable<HubDto> Hubs { get; init; }
}
