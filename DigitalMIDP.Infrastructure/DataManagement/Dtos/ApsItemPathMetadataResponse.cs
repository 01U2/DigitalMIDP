using System.Text.Json.Serialization;

namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

/// <summary>
/// DTO representing the APS API response for item path metadata.
/// Response from GET /data/v1/projects/{projectId}/items/{itemUrn}?includePathInProject=true
/// </summary>
internal sealed class ApsItemPathMetadataResponse
{
    [JsonPropertyName("data")]
    public required ApsItemPathData Data { get; init; }
}

/// <summary>
/// Item data section containing path and links.
/// </summary>
internal sealed class ApsItemPathData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("attributes")]
    public ApsItemPathAttributes? Attributes { get; init; }

    [JsonPropertyName("links")]
    public ApsItemPathLinks? Links { get; init; }
}

/// <summary>
/// Attributes section containing pathInProject.
/// </summary>
internal sealed class ApsItemPathAttributes
{
    [JsonPropertyName("pathInProject")]
    public string? PathInProject { get; init; }
}

/// <summary>
/// Links section containing webView URL.
/// </summary>
internal sealed class ApsItemPathLinks
{
    [JsonPropertyName("webView")]
    public ApsItemPathWebView? WebView { get; init; }
}

/// <summary>
/// Web view link object.
/// </summary>
internal sealed class ApsItemPathWebView
{
    [JsonPropertyName("href")]
    public string? Href { get; init; }
}
