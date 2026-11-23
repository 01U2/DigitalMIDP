using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Application.Manifests;

public static class ProjectManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RequireFields } }
    };

    private static void RequireFields(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (info.Type == typeof(ProjectManifest) || info.Type == typeof(ManifestFolder) ||
                info.Type == typeof(ManifestFile) || (info.Type == typeof(MetadataFieldDisplayDto) &&
                property.Name is "attributeId" or "name" or "type" or "optional" or "options"))
                property.IsRequired = true;
        }
    }

    public static string Serialize(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Validate(manifest);
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static ProjectManifest Deserialize(string json, string expectedHubId, string expectedProjectId, long? expectedRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHubId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedProjectId);
        if (expectedRevision is <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        using var document = JsonDocument.Parse(json);
        CheckProperties(document.RootElement);
        var manifest = document.RootElement.Deserialize<ProjectManifest>(Options);
        Require(manifest != null, "Expected a manifest object.");
        var published = document.RootElement.GetProperty("publishedAtUtc").GetString();
        Require(published != null && (published.EndsWith('Z') || published.EndsWith("+00:00", StringComparison.Ordinal)),
            "Publication time must specify UTC.");
        Require(manifest.HubId == expectedHubId && manifest.ProjectId == expectedProjectId, "Unexpected manifest identity.");
        Require(expectedRevision == null || expectedRevision == manifest.Revision, "Unexpected manifest revision.");
        Validate(manifest);
        return manifest;
    }

    private static void CheckProperties(JsonElement element, bool attributeKeys = false)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CheckProperties(child);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                string name;
                try { name = property.Name; }
                catch (InvalidOperationException ex) { throw new JsonException("Invalid JSON property name.", ex); }
                Require(names.Add(name), "Duplicate JSON property.");
                if (attributeKeys)
                    Require(int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
                        id > 0 && name == id.ToString(CultureInfo.InvariantCulture), "Invalid attribute ID key.");
                CheckProperties(property.Value, name == "values");
            }
        }
    }

    private static void Validate(ProjectManifest manifest)
    {
        Require(manifest.SchemaVersion == ProjectManifest.CurrentSchemaVersion, "Unsupported manifest schema version.");
        Require(Identifier(manifest.HubId) && Identifier(manifest.ProjectId), "Missing or invalid manifest identity.");
        Require(manifest.Revision > 0, "Revision must be positive.");
        Require(manifest.PublishedAtUtc != default && manifest.PublishedAtUtc.Offset == TimeSpan.Zero, "Invalid UTC publication time.");
        Require(manifest.Folders != null, "Missing folders.");
        var pathsById = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in manifest.Folders)
        {
            Require(folder != null && Identifier(folder.FolderId), "Invalid folder ID.");
            Require(Segment(folder.Name) && folder.PathInProject != null && RelativePath(folder.PathInProject), "Invalid folder path or name.");
            var path = folder.PathInProject.Replace('\\', '/');
            Require(path[(path.LastIndexOf('/') + 1)..] == folder.Name, "Folder path does not match its name.");
            Require(pathsById.TryAdd(folder.FolderId, path) && paths.Add(path), "Duplicate folder ID or path.");
            Require(folder.Definitions != null && folder.Files != null, "Missing folder collections.");
            var ids = new HashSet<int>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in folder.Definitions)
            {
                Require(field != null && field.AttributeId > 0 && ids.Add(field.AttributeId), "Invalid or duplicate folder attribute ID.");
                Require(Identifier(field.Name) && names.Add(field.Name) && Identifier(field.Type), "Invalid or duplicate field definition.");
                Require(field.Options != null && field.Options.All(option => option != null), "Invalid field options.");
            }
            foreach (var file in folder.Files)
            {
                Require(file?.Item != null && file.Values != null, "Missing file item or values.");
                var item = file.Item;
                Require(item.Type == ItemType.File && Identifier(item.Id) && fileIds.Add(item.Id), "Invalid or duplicate file.");
                Require(item.ParentId == folder.FolderId, "File parent does not match containing folder.");
                Require(file.Values.Keys.All(ids.Contains), "Value references an unknown folder attribute ID.");
                Require(item.Size is not < 0 && (item.LifecycleStage == null || Enum.IsDefined(item.LifecycleStage.Value)), "Invalid file metadata.");
                Require(Segment(item.Name) && RelativePath(item.Location) && RelativePath(item.PathInProject), "Invalid project path or file name.");
                Require(item.WebViewLink == null || WebLink(item.WebViewLink), "Invalid Autodesk web link.");
                if (item.CustomAttributes != null)
                {
                    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Require(item.CustomAttributes.Keys.All(key => Identifier(key) && keys.Add(key)), "Invalid custom attribute key.");
                }
            }
        }
        foreach (var folder in manifest.Folders)
        {
            if (folder.ParentFolderId == null) continue;
            Require(pathsById.TryGetValue(folder.ParentFolderId, out var parent), "Unknown parent folder.");
            Require(pathsById[folder.FolderId] == parent + "/" + folder.Name, "Folder path does not match its parent.");
        }
    }

    private static bool Identifier(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);

    private static bool RelativePath(string? path) => path == null || path.Split('/', '\\').All(Segment);

    private static bool Segment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))) return false;
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") &&
            !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            "0123456789¹²³".Contains(stem[3]));
    }

    private static bool WebLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.IsWellFormedOriginalString() && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        !value.Contains('\\') && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl) && uri.UserInfo.Length == 0 &&
        uri.IdnHost.EndsWith(".autodesk.com", StringComparison.OrdinalIgnoreCase);

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new JsonException(message);
    }
}
