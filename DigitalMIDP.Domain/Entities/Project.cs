namespace DigitalMIDP.Domain.Entities;

/// <summary>
/// Represents an Autodesk Forma Project within a Hub.
/// </summary>
public sealed record Project
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string HubId { get; init; }
    public string? Type { get; init; }

    public static Project Create(string id, string name, string hubId, string? type = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId);

        return new Project
        {
            Id = id,
            Name = name,
            HubId = hubId,
            Type = type
        };
    }
}
