namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Data Transfer Object for item path and web view metadata from APS.
/// Retrieved from GET /data/v1/projects/{projectId}/items/{itemUrn}?includePathInProject=true
/// </summary>
public sealed record ItemPathMetadataDto
{
    /// <summary>
    /// The item URN (ID) this metadata belongs to.
    /// </summary>
    public required string ItemUrn { get; init; }

    /// <summary>
    /// The relative path of the item within the project's file structure.
    /// Example: "Drawings/Architectural/Floor Plans/Level 1.dwg"
    /// </summary>
    public string? PathInProject { get; init; }

    /// <summary>
    /// The ACC web viewer URL for this item.
    /// Users can open this URL in a browser to view the file in Autodesk Forma.
    /// </summary>
    public string? WebViewLink { get; init; }
}
