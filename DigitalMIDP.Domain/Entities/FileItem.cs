namespace DigitalMIDP.Domain.Entities;

/// <summary>
/// Represents a file item in the APS Data Management API.
/// Named 'FileItem' to avoid conflict with System.IO.File.
/// </summary>
public sealed record FileItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProjectId { get; init; }
    public required string FolderId { get; init; }
    public string? DisplayName { get; init; }
    public string? FileType { get; init; }
    public string? VersionNumber { get; init; }
    public long? SizeInBytes { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ModifiedBy { get; init; }

    /// <summary>
    /// Custom attributes from APS (Status, Title, Revision, etc.).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }

    public static FileItem Create(
        string id,
        string name,
        string projectId,
        string folderId,
        string? displayName = null,
        string? fileType = null,
        string? versionNumber = null,
        long? sizeInBytes = null,
        DateTime? createdAt = null,
        DateTime? modifiedAt = null,
        string? createdBy = null,
        string? modifiedBy = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        return new FileItem
        {
            Id = id,
            Name = name,
            ProjectId = projectId,
            FolderId = folderId,
            DisplayName = displayName,
            FileType = fileType,
            VersionNumber = versionNumber,
            SizeInBytes = sizeInBytes,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            CreatedBy = createdBy,
            ModifiedBy = modifiedBy,
            Attributes = attributes
        };
    }
}
