using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Application.UseCases;

namespace DigitalMIDP.Infrastructure.DataManagement;

internal sealed partial class ApsManifestPublisher
{
    private async Task<string> ResolveTemplatesAsync(string hub, string project, CancellationToken ct)
    {
        var root = await ReadRootAsync(hub, project, ct);
        var parents = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var admin = await ResolveAsync(root.Id, FolderNameMatcher.IsHiddenAdminFolder, "00 ProjectFiles");
        return await ResolveAsync(admin, FolderNameMatcher.IsTemplateFolder, "Templates");

        async Task<string> ResolveAsync(string parent, Func<string?, bool> matches, string canonical)
        {
            async Task<ItemDto?> FindAsync() => (await ReadChildrenAsync(project, parent, ct)).SingleOrDefault(i => matches(i.Name));
            var match = await FindAsync();
            if (match == null)
            {
                var created = await storage.CreateFolderAsync(Part(project), parent, canonical, ct);
                match = await FindAsync();
                if (match?.Id != created.Id) throw new InvalidDataException($"Cannot verify created folder '{canonical}'.");
            }
            if (match!.Type != ItemType.Folder || !parents.Add(match.Id))
                throw new InvalidDataException($"Invalid {canonical} folder under {parent}.");
            return match.Id;
        }
    }

    private async Task<ItemDto?> FindManifestAsync(string project, string folder, CancellationToken ct)
    {
        var item = (await ReadChildrenAsync(project, folder, ct)).SingleOrDefault(i => ProjectManifest.IsReserved(i.Name));
        if (item != null && item.Type != ItemType.File) throw new InvalidDataException("Manifest name is occupied by a folder.");
        return item;
    }
}