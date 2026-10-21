using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Domain.Entities;
using DigitalMIDP.Infrastructure.DataManagement.Dtos;

namespace DigitalMIDP.Infrastructure.Mappings;

/// <summary>
/// Extension methods for mapping between API DTOs and Application/Domain entities.
/// </summary>
internal static class MappingExtensions
{
    /// <summary>
    /// Maps APS Hub API response to Application Hub DTO.
    /// </summary>
    public static HubDto ToApplicationDto(this ApsHubDto apsHub)
    {
        ArgumentNullException.ThrowIfNull(apsHub);

        return new HubDto
        {
            Id = apsHub.Id,
            Name = apsHub.Attributes.Name,
            Type = ExtractHubType(apsHub.Id),
            Region = apsHub.Attributes.Region
        };
    }

    /// <summary>
    /// Maps APS Project API response to Application Project DTO.
    /// </summary>
    public static ProjectDto ToApplicationDto(this ApsProjectDto apsProject)
    {
        ArgumentNullException.ThrowIfNull(apsProject);

        return new ProjectDto
        {
            Id = apsProject.Id,
            Name = apsProject.Attributes.Name,
            Description = apsProject.Attributes.Description
        };
    }

    /// <summary>
    /// Maps APS Folder API response to Application Folder DTO.
    /// </summary>
    public static FolderDto ToApplicationDto(this ApsFolderDto apsFolder)
    {
        ArgumentNullException.ThrowIfNull(apsFolder);

        return new FolderDto
        {
            Id = apsFolder.Id,
            Name = apsFolder.Attributes.DisplayName ?? apsFolder.Attributes.Name ?? string.Empty,
            ParentFolderId = apsFolder.Attributes.ParentId
        };
    }

    /// <summary>
    /// Maps APS Item API response to Application Item DTO.
    /// </summary>
    public static ItemDto ToApplicationDto(this ApsItemDto apsItem)
    {
        ArgumentNullException.ThrowIfNull(apsItem);

        var itemType = DetermineItemType(apsItem.Type);

        return new ItemDto
        {
            Id = apsItem.Id,
            Name = apsItem.Attributes.DisplayName ?? apsItem.Attributes.Name ?? string.Empty,
            Type = itemType,
            ParentId = apsItem.Attributes.ParentId,
            ItemUrn = itemType == ItemType.File ? apsItem.Id : null,
            Size = apsItem.Attributes.StorageSize,
            CreatedAt = apsItem.Attributes.CreateTime,
            ModifiedAt = apsItem.Attributes.LastModifiedTime
        };
    }

    /// <summary>
    /// Maps Application Hub DTO to Domain Hub entity.
    /// </summary>
    public static Hub ToEntity(this HubDto hubDto)
    {
        ArgumentNullException.ThrowIfNull(hubDto);

        return Hub.Create(hubDto.Id, hubDto.Name, hubDto.Type, hubDto.Region);
    }

    /// <summary>
    /// Maps Application Project DTO to Domain Project entity.
    /// </summary>
    public static Project ToEntity(this ProjectDto projectDto, string hubId, string? type = null)
    {
        ArgumentNullException.ThrowIfNull(projectDto);
        ArgumentException.ThrowIfNullOrWhiteSpace(hubId, nameof(hubId));

        // hubId is guaranteed non-null after validation
        return Project.Create(projectDto.Id, projectDto.Name, hubId, type);
    }

    /// <summary>
    /// Maps Application Folder DTO to Domain Folder entity.
    /// </summary>
    public static Folder ToEntity(this FolderDto folderDto, string projectId)
    {
        ArgumentNullException.ThrowIfNull(folderDto);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        // Previously passed ParentFolderId into the projectId slot (and dropped the parent). The
        // project is supplied by the caller, as in the ItemDto overload below.
        return Folder.Create(folderDto.Id, folderDto.Name, projectId, folderDto.ParentFolderId);
    }

    /// <summary>
    /// Maps Application Item DTO to Domain FileItem entity.
    /// </summary>
    public static FileItem ToEntity(this ItemDto itemDto, string projectId, string folderId)
    {
        ArgumentNullException.ThrowIfNull(itemDto);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        return FileItem.Create(itemDto.Id, itemDto.Name, projectId, folderId);
    }

    private static ItemType DetermineItemType(string apsItemType)
    {
        return apsItemType.Contains("Folder", StringComparison.OrdinalIgnoreCase)
            ? ItemType.Folder
            : ItemType.File;
    }

    private static string ExtractHubType(string hubId)
    {
        return hubId.StartsWith("a.", StringComparison.OrdinalIgnoreCase) ? "ACC" : "BIM360";
    }
}
