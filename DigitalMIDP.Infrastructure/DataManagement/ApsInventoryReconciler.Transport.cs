using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Infrastructure.DataManagement.Dtos;
using DigitalMIDP.Infrastructure.Mappings;

namespace DigitalMIDP.Infrastructure.DataManagement;

internal abstract class ApsInventoryReader(HttpClient client)
{
    private static readonly Uri Origin = new("https://developer.api.autodesk.com/");

    protected static string Part(string id) => !string.IsNullOrWhiteSpace(id) && id is not ("." or "..")
        ? Uri.EscapeDataString(id) : throw new InvalidDataException("Invalid APS resource ID.");

    protected async Task<JsonElement> ReadAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var body = document.RootElement;
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("errors", out var errors) && errors.GetArrayLength() != 0)
                throw new InvalidDataException("APS reported errors in inventory response.");
            return body.Clone();
        }
    }

    private async IAsyncEnumerable<JsonElement> Pages(string url, [EnumeratorCancellation] CancellationToken ct)
    {
        var first = new Uri(Origin, url);
        Uri? next = first;
        var visited = new HashSet<Uri>();
        while (next != null)
        {
            ct.ThrowIfCancellationRequested();
            if (visited.Count >= 10000 || !visited.Add(next) || next.Scheme != Origin.Scheme || next.Authority != Origin.Authority ||
                next.AbsolutePath != first.AbsolutePath || next.UserInfo.Length != 0 || next.Fragment.Length != 0)
                throw new InvalidDataException("Invalid, repeated or excessive APS page continuation.");
            var body = await ReadAsync(new HttpRequestMessage(HttpMethod.Get, next), ct);
            yield return body;
            string? href = null;
            if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var link) && link.ValueKind != JsonValueKind.Null)
                    href = Link(link);
                if (body.TryGetProperty("pagination", out var pagination))
                {
                    if (pagination.TryGetProperty("nextUrl", out var continuation) && continuation.ValueKind != JsonValueKind.Null)
                        href = Link(continuation);
                    if (href == null && pagination.TryGetProperty("totalResults", out var total) &&
                        (long)pagination.GetProperty("offset").GetInt32() + pagination.GetProperty("limit").GetInt32() < total.GetInt32())
                        throw new InvalidDataException("APS omitted a required next page.");
                }
            }
            next = href == null ? null : new Uri(next, href);
        }
    }

    private static string Link(JsonElement element) => (element.ValueKind == JsonValueKind.String ? element : element.GetProperty("href")).GetString()
        is string href && !string.IsNullOrWhiteSpace(href) ? href : throw new InvalidDataException("Empty APS link.");

    protected async Task<ItemDto> ReadRootAsync(string hubId, string projectId, CancellationToken ct)
    {
        var rows = await ReadItemsAsync($"project/v1/hubs/{Part(hubId)}/projects/{Part(projectId)}/topFolders?projectFilesOnly=true", ct);
        var root = rows.Single(i => i.Name.Equals("Project Files", StringComparison.OrdinalIgnoreCase));
        return root.Type == ItemType.Folder ? root : throw new InvalidDataException("Project Files is not a folder.");
    }

    protected Task<List<ItemDto>> ReadChildrenAsync(string projectId, string folderId, CancellationToken ct) =>
        ReadItemsAsync($"data/v1/projects/{Part(projectId)}/folders/{Part(folderId)}/contents?page[limit]=200&page[number]=0", ct);

    private async Task<List<ItemDto>> ReadItemsAsync(string url, CancellationToken ct)
    {
        var items = new List<ItemDto>();
        await foreach (var page in Pages(url, ct))
        foreach (var row in page.GetProperty("data").EnumerateArray())
        {
            var raw = row.Deserialize<ApsItemDto>();
            if (raw == null || raw.Type is not ("folders" or "items") || string.IsNullOrWhiteSpace(raw.Id) || raw.Attributes == null)
                throw new InvalidDataException("Invalid APS listing entry.");
            var item = raw.ToApplicationDto();
            if (string.IsNullOrWhiteSpace(item.Name)) throw new InvalidDataException("Missing APS display name.");
            var tip = raw.Relationships?.Tip?.Data?.Id;
            if (item.Type == ItemType.File && string.IsNullOrWhiteSpace(tip)) throw new InvalidDataException("Missing file tip: " + item.Id);
            var web = row.TryGetProperty("links", out var links) && links.TryGetProperty("webView", out var view) ? Link(view) : null;
            items.Add(item with { TipVersionUrn = tip, WebViewLink = web });
        }
        return items;
    }

    protected static string DocsProject(string projectId) => Part(projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? projectId[2..] : projectId);

    protected async Task<string?> ReadWebLinkAsync(string projectId, ItemDto item, CancellationToken ct)
    {
        if (item.WebViewLink != null) return item.WebViewLink;
        var url = new Uri(Origin, $"data/v1/projects/{Part(projectId)}/items/{Part(item.Id)}?includePathInProject=true");
        var data = (await ReadAsync(new HttpRequestMessage(HttpMethod.Get, url), ct)).GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object || (data.TryGetProperty("id", out var id) && id.GetString() != item.Id))
            throw new InvalidDataException("Unexpected APS item detail.");
        return data.TryGetProperty("links", out var links) && links.TryGetProperty("webView", out var view) ? Link(view) : null;
    }

    protected static List<MetadataFieldDisplayDto> MergeDefinitions(List<MetadataFieldDisplayDto> project, List<MetadataFieldDisplayDto> folder)
    {
        var fields = project.ToDictionary(d => d.AttributeId);
        foreach (var field in folder.ToDictionary(d => d.AttributeId).Values) fields[field.AttributeId] = field;
        return fields.Values.ToList();
    }

    protected async Task<List<MetadataFieldDisplayDto>> ReadDefinitionsAsync(string project, string? folder, CancellationToken ct)
    {
        var fields = new List<MetadataFieldDisplayDto>();
        var scope = folder == null ? string.Empty : $"/folders/{Part(folder)}";
        await foreach (var page in Pages($"bim360/docs/v1/projects/{project}{scope}/custom-attribute-definitions", ct))
        {
            var array = page.ValueKind == JsonValueKind.Array ? page : page.TryGetProperty("results", out var results) ? results :
                page.TryGetProperty("data", out var data) ? data : page.GetProperty("definitions");
            foreach (var row in array.EnumerateArray())
            {
                var field = row.Deserialize<CustomAttributeDefinitionDto>() ?? throw new InvalidDataException("Null attribute definition.");
                var options = (field.ArrayValues ?? []).Select(v => new MetadataOptionDisplayDto(v ?? throw new InvalidDataException("Null attribute option."), null)).ToArray();
                fields.Add(new(field.Name, field.Type, field.Description, field.Optional ?? !(field.Required ?? false), options, field.DefaultValue, checked((int)field.Id)));
            }
        }
        return fields;
    }

    protected async Task<Dictionary<string, IReadOnlyDictionary<int, string?>>> ReadBatchAsync(string project, ItemDto[] batch, List<MetadataFieldDisplayDto> schema, CancellationToken ct)
    {
        var byName = schema.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var byId = schema.ToDictionary(d => d.AttributeId);
        var pending = batch.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var body = await ReadAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(Origin, $"bim360/docs/v1/projects/{project}/versions:batch-get"))
            { Content = JsonContent.Create(new { urns = batch.Select(i => i.Id).ToArray() }) }, ct);
        var results = new Dictionary<string, IReadOnlyDictionary<int, string?>>(StringComparer.Ordinal);
        foreach (var row in body.GetProperty("results").EnumerateArray())
        {
            var id = row.GetProperty("itemUrn").GetString();
            if (id == null || !pending.Remove(id, out var item) || row.GetProperty("urn").GetString() != item.TipVersionUrn)
                throw new InvalidDataException("Unexpected, duplicate or stale batch result.");
            var values = new SortedDictionary<int, string?>();
            foreach (var attribute in row.GetProperty("customAttributes").EnumerateArray())
            {
                var value = attribute.GetProperty("value");
                var name = attribute.TryGetProperty("name", out var n) ? n.GetString() : null;
                var field = attribute.TryGetProperty("id", out var key) && key.ValueKind != JsonValueKind.Null
                    ? byId[key.GetInt32()] : byName[name ?? throw new InvalidDataException("Missing attribute identity.")];
                if (name != null && !string.Equals(name, field.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Batch attribute name does not match its definition ID.");
                if (values.ContainsKey(field.AttributeId)) throw new InvalidDataException("Duplicate batch attribute.");
                values.Add(field.AttributeId, value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                    _ => throw new InvalidDataException("Unsupported custom attribute value.")
                });
            }
            results.Add(id, values);
        }
        if (pending.Count != 0) throw new InvalidDataException("APS omitted batch results.");
        return results;
    }
}
