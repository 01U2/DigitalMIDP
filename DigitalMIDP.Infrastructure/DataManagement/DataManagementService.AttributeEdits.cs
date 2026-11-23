using DigitalMIDP.Application.Dtos;

namespace DigitalMIDP.Infrastructure.DataManagement;

internal sealed partial class DataManagementService
{
    public Task ValidateAttributeEditAsync(string projectId, AttributeEditTarget target,
        IReadOnlyList<CustomAttributeUpdateRequestDto> updates, CancellationToken cancellationToken = default) =>
        new EditValidator(_httpClient).ValidateAsync(projectId, target, updates, cancellationToken);

    private sealed class EditValidator(HttpClient client) : ApsInventoryReader(client)
    {
        public async Task ValidateAsync(string projectId, AttributeEditTarget target,
            IReadOnlyList<CustomAttributeUpdateRequestDto> updates, CancellationToken ct)
        {
            var item = target.Item;
            var url = $"{DataApiBaseUrl}/projects/{Part(projectId)}/items/{Part(item.Id)}";
            var body = await ReadAsync(new HttpRequestMessage(HttpMethod.Get, url), ct);
            var data = body.GetProperty("data");
            var links = data.GetProperty("relationships");
            if (data.GetProperty("type").GetString() != "items" || data.GetProperty("id").GetString() != item.ItemUrn ||
                links.GetProperty("parent").GetProperty("data").GetProperty("id").GetString() != item.ParentId ||
                links.GetProperty("tip").GetProperty("data").GetProperty("id").GetString() != item.TipVersionUrn)
                throw new InvalidOperationException("The file moved or its version changed. Refresh and reopen the editor.");

            var project = DocsProject(projectId);
            var definitions = MergeDefinitions(await ReadDefinitionsAsync(project, null, ct), await ReadDefinitionsAsync(project, item.ParentId!, ct));
            var schema = definitions.ToDictionary(d => d.AttributeId);
            var current = (await ReadBatchAsync(project, [item], definitions, ct))[item.Id];
            foreach (var update in updates)
            {
                var id = update.AttributeId;
                var before = target.Definitions.Single(d => d.AttributeId == id);
                if (!schema.TryGetValue(id, out var field) || field.Name != before.Name || field.Type != before.Type ||
                    field.Optional != before.Optional || !field.Options.Select(o => o.Value).ToHashSet().SetEquals(before.Options.Select(o => o.Value)))
                    throw new InvalidOperationException($"The definition or options for '{before.Name}' changed. Refresh and reopen the editor.");
                if (current.TryGetValue(id, out var value) != target.Values.TryGetValue(id, out var expected) || value != expected)
                    throw new InvalidOperationException($"'{before.Name}' changed since the editor opened. Refresh and try again.");
            }
        }
    }
}
