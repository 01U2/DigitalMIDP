namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Data Transfer Object for an Autodesk Platform Services (APS) Project.
/// A Project represents a BIM 360 Account or ACC Hub project.
/// </summary>
public sealed record ProjectDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
