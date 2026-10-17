namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// Data Transfer Object for an Autodesk Platform Services (APS) File or Folder item.
/// Represents a general item in the hierarchy.
/// </summary>
public sealed record ItemDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ItemType Type { get; init; }
    public string? ParentId { get; init; }
    public string? ItemUrn { get; init; }
    public string? Location { get; init; }
    public string? PathInProject { get; init; }
    public string? WebViewLink { get; init; }
    public long? Size { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
    /// <summary>
    /// Custom attributes retrieved from APS batch-get endpoint.
    /// Only populated for file items with valid version URNs.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? CustomAttributes { get; init; }
    /// <summary>
    /// URN of the item's tip (latest) version, required to target the version-scoped
    /// custom-attributes update endpoint. Populated alongside CustomAttributes.
    /// </summary>
    public string? TipVersionUrn { get; init; }
    /// <summary>
    /// Which ISO 19650 lifecycle stage this item falls under (WIP, Shared, Published, Archive), or
    /// null when its location doesn't match any of them. Derived from <see cref="Location"/> at
    /// listing time - see <c>FolderNameMatcher.ClassifyLifecycleStage</c> - not read from ACC
    /// directly, since ACC has no single "stage" field of its own.
    /// </summary>
    public CdeLifecycleStage? LifecycleStage { get; init; }
}

/// <summary>
/// Enumeration for the type of an APS item (folder or file).
/// </summary>
public enum ItemType
{
    Folder,
    File
}
