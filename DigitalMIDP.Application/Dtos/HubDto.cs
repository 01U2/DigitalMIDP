namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Data Transfer Object for an Autodesk Platform Services (APS) Hub.
/// A Hub is a container for projects (ACC, BIM 360, etc.).
/// </summary>
public sealed record HubDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Region { get; init; }
}
