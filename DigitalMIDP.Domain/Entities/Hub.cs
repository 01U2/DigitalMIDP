namespace DigitalMIDP.Domain.Entities;

/// <summary>
/// Represents an Autodesk Forma Hub.
/// A Hub is a container for projects (ACC, BIM 360, etc.).
/// </summary>
public sealed record Hub
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Region { get; init; }

    public static Hub Create(string id, string name, string type, string? region = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        return new Hub
        {
            Id = id,
            Name = name,
            Type = type,
            Region = region
        };
    }
}
