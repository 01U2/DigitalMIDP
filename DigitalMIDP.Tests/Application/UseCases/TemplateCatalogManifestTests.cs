using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.UseCases;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Application.UseCases;

public sealed class TemplateCatalogManifestTests
{
    [Fact]
    public async Task ReservedFileIsNeverOfferedAsATemplate()
    {
        var structure = Substitute.For<ICdeStructureUseCase>();
        var data = Substitute.For<IDataManagementService>();
        var folder = new FolderDto { Id = "t", Name = "Templates" };
        structure.GetStructureAsync("h", "p", "token", default).Returns(new CdeStructureDto(
            new Dictionary<CdeLifecycleStage, FolderDto>(), folder, []));
        ItemDto File(string name) => new() { Id = name, Name = name, Type = ItemType.File };
        data.GetFolderItemsAsync("p", "t", "token", default).Returns(new[] { File("DigitalMIDP-Manifest.JSON"), File("template.json") });
        var catalog = new TemplateCatalogUseCase(structure, data, NullLogger<TemplateCatalogUseCase>.Instance);
        Assert.Equal("template.json", Assert.Single(await catalog.GetTemplatesAsync("h", "p", "token")).Name);
    }
}