namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalMIDP.Application.Dtos;

/// <summary>
/// Shape returned by GET /bim360/docs/v1/projects/{projectId}/naming-standards/{id}.
/// "fields" are the attributes that literally compose the filename code, in pattern order (e.g.
/// Project-Originator-Volume-Level-Type-Role-Number for ISO 19650). "metadata" are attributes
/// registered under the same standard but NOT part of the filename (e.g. Status, Revision,
/// Classification, Title) - those stay independently editable.
/// </summary>
public sealed class NamingStandardDefinitionDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("definition")]
    public NamingStandardDefinitionBodyDto? Definition { get; set; }

    /// <summary>
    /// The filename-code components, wherever "fields" actually appears in the response - real
    /// observed responses nest it under "definition", but this falls back to a top-level "fields"
    /// too in case that ever changes. Computed, not bound directly - JsonIgnore avoids colliding
    /// with <see cref="TopLevelFields"/>, which IS bound to the JSON "fields" key.
    /// </summary>
    [JsonIgnore]
    public NamingStandardComponentDto[]? Fields => Definition?.Fields ?? TopLevelFields;

    /// <summary>
    /// The non-filename attributes registered under this standard, wherever "metadata" appears.
    /// Same fallback reasoning as <see cref="Fields"/>.
    /// </summary>
    [JsonIgnore]
    public NamingStandardComponentDto[]? Metadata => Definition?.Metadata ?? TopLevelMetadata;

    /// <summary>
    /// The segment separator. Defaults to "-" when the response omits it, which matches the
    /// ISO 19650 pattern this app is built against.
    /// </summary>
    [JsonIgnore]
    public string ResolvedDelimiter =>
        string.IsNullOrEmpty(Definition?.Delimiter) ? NamingConventionDto.DefaultDelimiter : Definition.Delimiter;

    [JsonPropertyName("fields")]
    public NamingStandardComponentDto[]? TopLevelFields { get; set; }

    [JsonPropertyName("metadata")]
    public NamingStandardComponentDto[]? TopLevelMetadata { get; set; }
}

public sealed class NamingStandardDefinitionBodyDto
{
    [JsonPropertyName("delimiter")]
    public string? Delimiter { get; set; }

    [JsonPropertyName("fields")]
    public NamingStandardComponentDto[]? Fields { get; set; }

    [JsonPropertyName("metadata")]
    public NamingStandardComponentDto[]? Metadata { get; set; }
}

/// <summary>
/// A single attribute referenced by a naming standard. Different ACC endpoints have used "id" and
/// "attributeId" for the same concept elsewhere in this app (see CustomAttributeDefinitionDto vs.
/// the older naming-standard metadata shape), so both are accepted defensively here.
/// </summary>
public sealed class NamingStandardComponentDto
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("attributeId")]
    public long? AttributeId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("optional")]
    public bool? Optional { get; set; }

    /// <summary>
    /// Defensive fallback for a response that reports this the other way round, as "required" rather
    /// than "optional" - see the matching property on <c>CustomAttributeDefinitionDto</c> for why both
    /// schema sources in this codebase are read defensively for this flag.
    /// </summary>
    [JsonPropertyName("required")]
    public bool? Required { get; set; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Allowed values, under whichever key this response happens to use. Held as raw
    /// <see cref="JsonElement"/> rather than a typed array because the element shape is not fixed:
    /// ACC has been observed returning both bare strings and objects, and this app has repeatedly
    /// found undocumented Autodesk shapes to differ from the published docs. See
    /// <see cref="ResolveOptions"/> for the normalisation.
    /// </summary>
    [JsonPropertyName("values")]
    public JsonElement? Values { get; set; }

    [JsonPropertyName("arrayValues")]
    public JsonElement? ArrayValues { get; set; }

    [JsonPropertyName("options")]
    public JsonElement? Options { get; set; }

    [JsonPropertyName("allowedValues")]
    public JsonElement? AllowedValues { get; set; }

    /// <summary>
    /// Length constraints the project administrator configured for this field - ACC's naming standard
    /// editor lets a field be, for example, 2 to 6 characters, and rejects a document number whose
    /// segment falls outside that.
    ///
    /// Held under several possible keys for the same reason the option arrays are: the exact naming
    /// has not been confirmed against a live response, and getting it wrong should mean "no
    /// constraint known" rather than a crash. <see cref="ResolveMinLength"/> and
    /// <see cref="ResolveMaxLength"/> normalise whichever the response actually uses.
    /// </summary>
    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("minLen")]
    public int? MinLen { get; set; }

    [JsonPropertyName("maxLen")]
    public int? MaxLen { get; set; }

    /// <summary>A fixed width, where the field is exactly N characters rather than a range.</summary>
    [JsonPropertyName("length")]
    public JsonElement? Length { get; set; }

    public long ResolvedId => Id is > 0 ? Id.Value : (AttributeId ?? 0);

    /// <summary>
    /// The minimum number of characters ACC accepts for this segment, or null when unconstrained.
    /// </summary>
    public int? ResolveMinLength() =>
        MinLength ?? MinLen ?? ReadLengthMember("min", "minLength") ?? ReadFixedLength();

    /// <summary>
    /// The maximum number of characters ACC accepts for this segment, or null when unconstrained.
    /// </summary>
    public int? ResolveMaxLength() =>
        MaxLength ?? MaxLen ?? ReadLengthMember("max", "maxLength") ?? ReadFixedLength();

    /// <summary>Reads a bound from a nested "length" object, e.g. <c>"length": { "min": 2, "max": 6 }</c>.</summary>
    private int? ReadLengthMember(params string[] names)
    {
        if (Length is not { ValueKind: JsonValueKind.Object } lengthObject)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (lengthObject.TryGetProperty(name, out var member) &&
                member.ValueKind == JsonValueKind.Number &&
                member.TryGetInt32(out var value) &&
                value > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Reads a scalar "length", which fixes the segment to exactly that many characters.</summary>
    private int? ReadFixedLength() =>
        Length is { ValueKind: JsonValueKind.Number } scalar && scalar.TryGetInt32(out var fixedLength) && fixedLength > 0
            ? fixedLength
            : null;

    /// <summary>
    /// Normalises whichever option array the response carried into code/description pairs.
    ///
    /// The description half matters well beyond display: discipline folder names are built from it
    /// ("AR" + "Architect" gives the folder "AR Architect"), so a response that only yields codes
    /// leaves folder resolution with nothing to match on. Returns an empty list for free-text
    /// fields, which legitimately have no options.
    /// </summary>
    public IReadOnlyList<MetadataOptionDisplayDto> ResolveOptions()
    {
        foreach (var candidate in new[] { Values, ArrayValues, Options, AllowedValues })
        {
            if (candidate is not { ValueKind: JsonValueKind.Array } array)
            {
                continue;
            }

            var options = array.EnumerateArray()
                .Select(ReadOption)
                .Where(o => !string.IsNullOrWhiteSpace(o?.Value))
                .Select(o => o!)
                .ToList();

            if (options.Count > 0)
            {
                return options;
            }
        }

        return [];
    }

    /// <summary>
    /// Reads one option element. A bare string is a code with no description; an object is searched
    /// for a code key and a description key under the several names ACC has used for them.
    /// </summary>
    private static MetadataOptionDisplayDto? ReadOption(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new MetadataOptionDisplayDto(element.GetString(), null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var value = ReadFirstString(element, "value", "code", "key", "id", "name");
        var description = ReadFirstString(element, "description", "label", "title", "displayName", "text");

        // When only one text field is present it is the code, not the description - an option
        // without a code is unusable, whereas one without a description is merely less informative.
        if (string.IsNullOrWhiteSpace(value))
        {
            value = description;
            description = null;
        }

        return value is null ? null : new MetadataOptionDisplayDto(value, description);
    }

    private static string? ReadFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
