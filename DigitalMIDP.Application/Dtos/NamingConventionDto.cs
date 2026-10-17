namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// A project's file naming convention as configured in ACC, expressed from the perspective of
/// <em>creating</em> a file rather than displaying one.
///
/// This is the deliberate counterpart to <see cref="ProjectNamingStandardDto"/>. That type carries
/// the attributes that are NOT part of the filename (it exists to drive the file grid's dynamic
/// columns, and so filters the filename components out on purpose). This type carries the filename
/// components themselves, <b>in pattern order</b>, because composing a new document number requires
/// exactly the data the other one throws away.
/// </summary>
/// <param name="Id">The ACC naming standard id, or empty when the project has none configured.</param>
/// <param name="Name">The naming standard's display name.</param>
/// <param name="Delimiter">Separator between filename segments (typically "-").</param>
/// <param name="Fields">
/// The filename segments in pattern order, e.g. Project, Originator, Volume/System, Level/Location,
/// Type, Role, Number. Empty when no naming standard is configured for the project.
/// </param>
/// <param name="Metadata">
/// Attributes registered under the same standard but not part of the filename - Status, Revision,
/// Description, Title and similar. Description supplies the Deliverable Title entered for each file.
/// </param>
public sealed record NamingConventionDto(
    string Id,
    string Name,
    string Delimiter,
    IReadOnlyList<NamingConventionFieldDto> Fields,
    IReadOnlyList<MetadataFieldDisplayDto> Metadata
)
{
    /// <summary>Placeholder for a project with no naming standard configured.</summary>
    public static NamingConventionDto None { get; } =
        new(string.Empty, "No Standard", DefaultDelimiter, [], []);

    public const string DefaultDelimiter = "-";

    /// <summary>
    /// False when ACC returned no naming standard, or one that parsed with zero filename fields.
    /// Deliverable creation cannot proceed in that state - there is no pattern to build a document
    /// number from, and inventing one would produce files that violate the project's standard.
    /// </summary>
    public bool IsConfigured => Fields.Count > 0;

    /// <summary>
    /// The trailing sequential field the application allocates itself (0001, 0002, ...), as opposed
    /// to the segments the user chooses. Resolved by name where possible, otherwise by position:
    /// the convention places it last, which is what the client's documented pattern
    /// (Project-Originator-Volume-Level-Type-Role-<b>Number</b>) does.
    /// </summary>
    public NamingConventionFieldDto? SequentialNumberField =>
        Fields.FirstOrDefault(f => f.LooksLikeSequentialNumber) ?? Fields.LastOrDefault();

    /// <summary>
    /// The field that identifies the discipline. The client confirmed discipline and role are the
    /// same thing, and the WIP destination folder is derived from this field's chosen option, so
    /// resolving it is what makes <c>WIP/{Discipline}/</c> computable rather than user-picked.
    /// </summary>
    public NamingConventionFieldDto? DisciplineField =>
        Fields.FirstOrDefault(f => f.LooksLikeDiscipline);

    /// <summary>
    /// The field holding the deliverable type (Drawing, Report, 3D Model). Drives step 1 of the
    /// creation wizard, which must offer the project's configured types rather than a fixed list.
    /// </summary>
    public NamingConventionFieldDto? TypeField =>
        Fields.FirstOrDefault(f => f.LooksLikeDeliverableType);

    /// <summary>
    /// The fields the user must supply values for - everything except the sequential number, which
    /// the application allocates.
    /// </summary>
    public IReadOnlyList<NamingConventionFieldDto> UserSuppliedFields
    {
        get
        {
            var sequential = SequentialNumberField;
            return Fields.Where(f => f.AttributeId != sequential?.AttributeId).ToList();
        }
    }
}

/// <summary>
/// One segment of the filename pattern - a custom attribute plus its allowed values.
/// </summary>
/// <param name="AttributeId">ACC custom attribute definition id. The stable identity of the field.</param>
/// <param name="Name">Display name, e.g. "Role".</param>
/// <param name="Type">ACC attribute type, e.g. "array" for a fixed option list, "string" for free text.</param>
/// <param name="Optional">Whether ACC treats the attribute as optional.</param>
/// <param name="Position">Zero-based index within the filename pattern.</param>
/// <param name="Options">
/// Allowed values. Each carries a code and, where ACC supplies one, a description - the pairing that
/// discipline folder names are built from ("AR" + "Architect" gives the folder "AR Architect").
/// Empty for free-text fields.
/// </param>
/// <param name="DefaultValue">Default supplied by ACC, if any.</param>
/// <param name="MinLength">
/// Fewest characters ACC accepts for this segment, or null when the project sets no lower bound.
/// Configured per field in ACC's naming standard editor - a Project code might be 2 to 6 characters.
/// </param>
/// <param name="MaxLength">Most characters ACC accepts, or null when unbounded.</param>
public sealed record NamingConventionFieldDto(
    int AttributeId,
    string Name,
    string Type,
    bool Optional,
    int Position,
    IReadOnlyList<MetadataOptionDisplayDto> Options,
    string? DefaultValue,
    int? MinLength = null,
    int? MaxLength = null
)
{
    /// <summary>True when the field constrains input to a fixed option list.</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>True when ACC constrains this segment's length in either direction.</summary>
    public bool HasLengthConstraint => MinLength is > 0 || MaxLength is > 0;

    /// <summary>
    /// The length rule phrased for the user, e.g. "2-6 characters", or null when unconstrained.
    /// Shown as a hint under the input so the rule is visible before it is broken.
    /// </summary>
    public string? LengthHint => (MinLength, MaxLength) switch
    {
        (> 0, > 0) when MinLength == MaxLength => $"exactly {MinLength} characters",
        (> 0, > 0) => $"{MinLength}-{MaxLength} characters",
        (> 0, _) => $"at least {MinLength} characters",
        (_, > 0) => $"up to {MaxLength} characters",
        _ => null
    };

    internal bool LooksLikeSequentialNumber => NameContainsAny("number", "sequence", "serial");

    internal bool LooksLikeDiscipline => NameContainsAny("role", "discipline");

    internal bool LooksLikeDeliverableType => NameContainsAny("type");

    private bool NameContainsAny(params string[] tokens) =>
        tokens.Any(t => Name.Contains(t, StringComparison.OrdinalIgnoreCase));
}
