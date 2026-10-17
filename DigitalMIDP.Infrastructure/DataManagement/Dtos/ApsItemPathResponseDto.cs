using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// DTO representing an APS item response with path and web view link.
/// </summary>
internal sealed class ApsItemPathResponseDto
{
    [JsonPropertyName("data")]
    public ItemData? Data { get; init; }

    internal sealed class ItemData
    {
        [JsonPropertyName("attributes")]
        public ItemAttributes? Attributes { get; init; }

        [JsonPropertyName("links")]
        public ItemLinks? Links { get; init; }
    }

    internal sealed class ItemAttributes
    {
        [JsonPropertyName("pathInProject")]
        public JsonElement PathInProject { get; init; }
    }

    internal sealed class ItemLinks
    {
        [JsonPropertyName("webView")]
        public ItemLink? WebView { get; init; }
    }

    internal sealed class ItemLink
    {
        [JsonPropertyName("href")]
        public string? Href { get; init; }
    }
}
