namespace DigitalMIDP.Application.Dtos;

/// <summary>
/// A single resolved segment of a document number - one naming-convention field and the value chosen
/// for it.
/// </summary>
/// <param name="AttributeId">The ACC custom attribute definition id this segment came from.</param>
/// <param name="FieldName">The field's display name, e.g. "Role".</param>
/// <param name="Value">The chosen code, e.g. "AR".</param>
public sealed record DocumentNumberSegmentDto(int AttributeId, string FieldName, string Value);

/// <summary>
/// A composed document number - the structured identifier that is the authoritative identity of a
/// deliverable.
///
/// The number is deliberately kept as its segments plus a delimiter rather than as a bare string,
/// because uniqueness is checked against a file's <em>custom attribute values</em>, not against its
/// filename. A file can be renamed in ACC while still carrying the same document number in its
/// attributes, and a filename-only comparison would miss that.
/// </summary>
/// <param name="Segments">The segments in filename-pattern order.</param>
/// <param name="Delimiter">Separator between segments, from the naming convention.</param>
public sealed record DocumentNumberDto(
    IReadOnlyList<DocumentNumberSegmentDto> Segments,
    string Delimiter
)
{
    /// <summary>
    /// The number as it appears in a filename, e.g. "P01-JIA-ZZ-00-DR-AR-001".
    /// </summary>
    public string Value => string.Join(Delimiter, Segments.Select(s => s.Value));

    /// <summary>
    /// The segments excluding the trailing sequential number, which is what identifies a "family" of
    /// deliverables sharing everything but their sequence. Two numbers collide only when their
    /// prefixes match, so this is the key the allocator groups existing files by.
    /// </summary>
    public string Prefix => Segments.Count <= 1
        ? string.Empty
        : string.Join(Delimiter, Segments.Take(Segments.Count - 1).Select(s => s.Value));

    /// <summary>The trailing sequential segment, e.g. "001".</summary>
    public string Sequence => Segments.Count == 0 ? string.Empty : Segments[^1].Value;

    public override string ToString() => Value;
}

/// <summary>
/// The outcome of checking whether a proposed document number can be used.
/// </summary>
/// <param name="ProposedNumber">The number that was checked.</param>
/// <param name="IsAvailable">True when no existing deliverable in scope already carries this number.</param>
/// <param name="ConflictingFileName">The name of the file that already uses the number, when one was found.</param>
/// <param name="SuggestedNumber">
/// The next free number, supplied whenever the proposed one is taken. The agreed behaviour is to
/// refuse creation and offer this instead - if "ABC-AAB-XXX-001" exists, "ABC-AAB-XXX-002" is
/// proposed.
/// </param>
public sealed record DocumentNumberAvailabilityDto(
    string ProposedNumber,
    bool IsAvailable,
    string? ConflictingFileName,
    string? SuggestedNumber
)
{
    public static DocumentNumberAvailabilityDto Available(string number) =>
        new(number, IsAvailable: true, ConflictingFileName: null, SuggestedNumber: null);
}

/// <summary>
/// The result of allocating a document number and confirming it is free, in one pass.
///
/// <para>
/// This pairing exists to collapse what used to be two independent folder scans. Allocating a number
/// and then verifying it required reading the discipline folder's contents and every file's
/// attributes twice in immediate succession, with no work in between - so the second read was a
/// duplicate of the first by construction, not a genuine re-check against newer state. One scan now
/// produces both answers. See <see cref="IDocumentNumberUseCase.AllocateAndVerifyNextAsync"/> for
/// what that does and does not protect against.
/// </para>
/// </summary>
/// <param name="Number">The allocated number, free as of the scan it was allocated from.</param>
/// <param name="Availability">
/// The verification of <paramref name="Number"/>. Normally available - the allocator picks a number
/// above everything it just read - but carried explicitly so a caller handles the conflict case
/// identically whether it was detected here or by a separate check.
/// </param>
public sealed record DocumentNumberAllocationDto(
    DocumentNumberDto Number,
    DocumentNumberAvailabilityDto Availability
)
{
    /// <summary>True when the allocated number can be used as-is.</summary>
    public bool IsUsable => Availability.IsAvailable;
}
