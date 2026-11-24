using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.Manifests;

public sealed record ProjectManifest(
    int SchemaVersion,
    string HubId,
    string ProjectId,
    long Revision,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<ManifestFolder> Folders)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "digitalmidp-manifest.json";
    public static bool IsReserved(string? name) => string.Equals(name, FileName, StringComparison.OrdinalIgnoreCase);
}

public sealed record ManifestFolder(
    string FolderId,
    string Name,
    string? ParentFolderId,
    string PathInProject,
    IReadOnlyList<MetadataFieldDisplayDto> Definitions,
    IReadOnlyList<ManifestFile> Files);

public sealed record ManifestFile(ItemDto Item, IReadOnlyDictionary<int, string?> Values);
