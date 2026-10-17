namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Response DTO for the get projects use case.
/// </summary>
public sealed record GetProjectsResponseDto
{
    public required IEnumerable<ProjectDto> Projects { get; init; }
}
