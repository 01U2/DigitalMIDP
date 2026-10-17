namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Data Transfer Object for an Autodesk Platform Services (APS) Folder.
/// Folders are hierarchical containers for files and subfolders.
/// </summary>
public sealed record FolderDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ParentFolderId { get; init; }
}
