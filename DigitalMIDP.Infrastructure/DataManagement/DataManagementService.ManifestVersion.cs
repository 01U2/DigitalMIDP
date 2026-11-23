using System.Text.Json;
using DigitalMIDP.Application.Manifests;

namespace DigitalMIDP.Infrastructure.DataManagement;

internal sealed partial class DataManagementService
{
    internal async Task PublishVersionAsync(string project, string folder, string item, byte[] bytes, CancellationToken ct)
    {
        var storage = await CreateStorageAsync(project, folder, ProjectManifest.FileName, ct);
        await UploadToStorageAsync(storage, bytes, ct);
        var body = new
        {
            jsonapi = new { version = "1.0" },
            data = new
            {
                type = "versions",
                attributes = new { name = ProjectManifest.FileName, extension = new { type = VersionExtensionType, version = "1.0" } },
                relationships = new
                {
                    item = new { data = new { type = "items", id = item } },
                    storage = new { data = new { type = "objects", id = storage } }
                }
            }
        };
        var json = await SendJsonApiAsync($"{DataApiBaseUrl}/projects/{project}/versions", body, "publish manifest version", ct);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetProperty("type").GetString() != "versions" || string.IsNullOrWhiteSpace(data.GetProperty("id").GetString()))
            throw new InvalidDataException("APS did not confirm the published version.");
    }
}