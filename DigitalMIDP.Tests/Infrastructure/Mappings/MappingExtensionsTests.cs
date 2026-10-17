using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Infrastructure.DataManagement.Dtos;
using DigitalMIDP.Infrastructure.Mappings;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.Mappings;

public sealed class MappingExtensionsTests
{
    [Fact]
    public void ToApplicationDto_WithApsHubDto_MapsFields()
    {
        var apsHub = new ApsHubDto
        {
            Id = "a.hub1",
            Attributes = new ApsHubDto.HubAttributes
            {
                Name = "Hub One",
                Region = "US"
            }
        };

        var result = apsHub.ToApplicationDto();

        result.Should().BeEquivalentTo(new HubDto
        {
            Id = "a.hub1",
            Name = "Hub One",
            Type = "ACC",
            Region = "US"
        });
    }

    [Fact]
    public void ToApplicationDto_WithApsProjectDto_MapsFields()
    {
        var apsProject = new ApsProjectDto
        {
            Id = "project1",
            Attributes = new ApsProjectDto.ProjectAttributes
            {
                Name = "Project One",
                Description = "Demo"
            }
        };

        var result = apsProject.ToApplicationDto();

        result.Should().BeEquivalentTo(new ProjectDto
        {
            Id = "project1",
            Name = "Project One",
            Description = "Demo"
        });
    }

    [Fact]
    public void ToApplicationDto_WithApsFolderDto_UsesDisplayNameWhenPresent()
    {
        var apsFolder = new ApsFolderDto
        {
            Id = "folder1",
            Attributes = new ApsFolderDto.FolderAttributes
            {
                Name = "Folder One",
                DisplayName = "Display Folder",
                ParentId = "parent1"
            }
        };

        var result = apsFolder.ToApplicationDto();

        result.Should().BeEquivalentTo(new FolderDto
        {
            Id = "folder1",
            Name = "Display Folder",
            ParentFolderId = "parent1"
        });
    }

    [Fact]
    public void ToApplicationDto_WithApsItemDto_FileMapsUrnAndType()
    {
        var apsItem = new ApsItemDto
        {
            Id = "item1",
            Type = "items:autodesk.core:File",
            Attributes = new ApsItemDto.ItemAttributes
            {
                Name = "File One",
                DisplayName = "File One",
                ParentId = "parent1",
                StorageSize = 2048
            },
            Relationships = new ApsItemDto.ItemRelationships
            {
                Tip = new ApsItemDto.ItemRelationshipData
                {
                    Data = new ApsItemDto.ItemRelationshipResource
                    {
                        Id = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId"
                    }
                }
            }
        };

        var result = apsItem.ToApplicationDto();

        result.Should().BeEquivalentTo(new ItemDto
        {
            Id = "item1",
            Name = "File One",
            Type = ItemType.File,
            ParentId = "parent1",
            ItemUrn = "item1",
            Size = 2048
        });
    }

    [Fact]
    public void ToApplicationDto_WithApsItemDto_FolderMapsFolderType()
    {
        var apsItem = new ApsItemDto
        {
            Id = "folder1",
            Type = "items:autodesk.core:Folder",
            Attributes = new ApsItemDto.ItemAttributes
            {
                Name = "Folder One",
                DisplayName = "Folder One",
                ParentId = "parent1"
            }
        };

        var result = apsItem.ToApplicationDto();

        result.Should().BeEquivalentTo(new ItemDto
        {
            Id = "folder1",
            Name = "Folder One",
            Type = ItemType.Folder,
            ParentId = "parent1"
        });
    }

    [Fact]
    public void ToApplicationDto_WithNullHub_ThrowsArgumentNullException()
    {
        ApsHubDto? apsHub = null;

        var act = () => apsHub!.ToApplicationDto();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToApplicationDto_WithNullProject_ThrowsArgumentNullException()
    {
        ApsProjectDto? apsProject = null;

        var act = () => apsProject!.ToApplicationDto();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToApplicationDto_WithNullFolder_ThrowsArgumentNullException()
    {
        ApsFolderDto? apsFolder = null;

        var act = () => apsFolder!.ToApplicationDto();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToApplicationDto_WithNullItem_ThrowsArgumentNullException()
    {
        ApsItemDto? apsItem = null;

        var act = () => apsItem!.ToApplicationDto();

        act.Should().Throw<ArgumentNullException>();
    }
}
