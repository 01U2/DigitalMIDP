namespace DigitalMIDP.Domain.Entities;

/// <summary>
/// Represents a folder in the APS Data Management API.
/// </summary>
public sealed record Folder
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProjectId { get; init; }
    public string? ParentFolderId { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }

    public static Folder Create(
        string id,
        string name,
        string projectId,
        string? parentFolderId = null,
        DateTime? createdAt = null,
        DateTime? modifiedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        return new Folder
        {
            Id = id,
            Name = name,
            ProjectId = projectId,
            ParentFolderId = parentFolderId,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt
        };
    }
}
