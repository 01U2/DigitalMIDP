using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Concrete implementation of <see cref="IDocumentNumberUseCase"/>.
/// </summary>
public sealed class DocumentNumberUseCase : IDocumentNumberUseCase
{
    /// <summary>
    /// Digits in the sequential segment: numbering starts at 0001.
    ///
    /// This is a floor, not merely a fallback. A project whose existing files use a wider sequence
    /// keeps that width (see <see cref="ResolveSequenceWidth"/>), but a narrower one does not drag
    /// new numbers back down with it - a folder containing an earlier 3-digit "002" would otherwise
    /// keep producing 3-digit numbers that the naming standard rejects as non-conforming.
    /// </summary>
    private const int MinimumSequenceWidth = 4;

    private const int FirstSequence = 1;

    private readonly IDataManagementService _dataManagementService;
    private readonly ILogger<DocumentNumberUseCase> _logger;

    public DocumentNumberUseCase(
        IDataManagementService dataManagementService,
        ILogger<DocumentNumberUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(dataManagementService);
        ArgumentNullException.ThrowIfNull(logger);

        _dataManagementService = dataManagementService;
        _logger = logger;
    }

    public DocumentNumberDto Compose(
        NamingConventionDto convention,
        IReadOnlyDictionary<int, string> chosenValues,
        string sequence)
    {
        ArgumentNullException.ThrowIfNull(convention);
        ArgumentNullException.ThrowIfNull(chosenValues);

        if (!convention.IsConfigured)
        {
            throw new Exceptions.ApplicationException(
                "The project has no naming convention configured, so a document number cannot be composed.");
        }

        var sequentialField = convention.SequentialNumberField;
        var segments = new List<DocumentNumberSegmentDto>(convention.Fields.Count);
        var missing = new List<string>();

        foreach (var field in convention.Fields)
        {
            if (field.AttributeId == sequentialField?.AttributeId)
            {
                segments.Add(new DocumentNumberSegmentDto(field.AttributeId, field.Name, sequence));
                continue;
            }

            if (chosenValues.TryGetValue(field.AttributeId, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                // Enforced here as well as in the UI: a value carrying the delimiter or a space
                // corrupts the number's structure, and this is the last point before it becomes a
                // real file that cannot be renamed away.
                var problem = DocumentNumberSegmentValidator.Validate(
                    field.Name, value, convention.Delimiter, field.MinLength, field.MaxLength);
                if (problem is not null)
                {
                    throw new Exceptions.AttributeValidationException(problem);
                }

                segments.Add(new DocumentNumberSegmentDto(field.AttributeId, field.Name, value.Trim()));
                continue;
            }

            if (!field.Optional)
            {
                missing.Add(field.Name);
            }

            segments.Add(new DocumentNumberSegmentDto(field.AttributeId, field.Name, string.Empty));
        }

        if (missing.Count > 0)
        {
            throw new Exceptions.AttributeValidationException(
                $"Cannot compose a document number - required field(s) have no value: {string.Join(", ", missing)}.");
        }

        return new DocumentNumberDto(segments, convention.Delimiter);
    }

    public async Task<DocumentNumberDto> AllocateNextAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        IReadOnlyDictionary<int, string> chosenValues,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var allocation = await AllocateAndVerifyNextAsync(
            projectId, discipline, convention, chosenValues, [], accessToken, cancellationToken);

        return allocation.Number;
    }

    public async Task<DocumentNumberAllocationDto> AllocateAndVerifyNextAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        IReadOnlyDictionary<int, string> chosenValues,
        IReadOnlyCollection<string> reservedNumbers,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(discipline);
        ArgumentNullException.ThrowIfNull(convention);
        ArgumentNullException.ThrowIfNull(reservedNumbers);

        // The single scan. Everything below is computed from it - no second round trip.
        var scanned = await CollectExistingNumbersAsync(
            projectId, discipline, convention, accessToken, cancellationToken);

        // Numbers issued earlier in this batch are folded in as if they had been found by the scan,
        // so the next sequence steps past them even when ACC has not indexed the files yet. Each is
        // parsed the same way a scanned number is, so it lands in its own numbering group and also
        // contributes to that group's resolved sequence width.
        var existing = reservedNumbers.Count == 0
            ? scanned
            : scanned
                .Concat(reservedNumbers
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => new ExistingDocumentNumber(
                        n,
                        $"(reserved earlier in this batch)",
                        ExtractGroupKey(n, convention.Delimiter),
                        ExtractSequence(n, convention.Delimiter))))
                .ToList();

        // The composite key that determines the numbering group: every naming-convention segment
        // except the trailing sequential one. A document number is only "next" relative to the
        // files that share its prefix - a folder holding DR-ARC-S0-0007 and DR-ARC-S1-0025 must
        // allocate the next S0 number as 0008, not 0026 - so the maximum and the width are taken
        // from this group alone, never from the folder as a whole.
        var groupKey = Compose(convention, chosenValues, FormatSequence(FirstSequence, MinimumSequenceWidth)).Prefix;

        var group = existing
            .Where(e => string.Equals(e.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var width = ResolveSequenceWidth(group, convention.Delimiter);
        var next = group.Count == 0
            ? FirstSequence
            : group.Max(e => e.Sequence ?? 0) + 1;

        var number = Compose(convention, chosenValues, FormatSequence(next, width));

        _logger.LogInformation(
            "Allocated document number {Number} for discipline '{Discipline}' ({ScannedCount} existing deliverable(s) scanned, {ReservedCount} reserved in-batch)",
            number.Value, discipline.ExpectedFolderName, scanned.Count, reservedNumbers.Count);

        var availability = EvaluateAvailability(existing, convention, number);

        if (!availability.IsAvailable)
        {
            // Reaching here means the number just allocated is already in use, which should be
            // impossible: it was derived as one past the highest number in the very list being
            // checked. It is left as a real check rather than an assertion because the composition
            // step can legitimately produce a different prefix than the scanned files carry, and
            // reporting a conflict is the safe outcome either way.
            _logger.LogWarning(
                "Allocated number {Number} collided with existing file '{ConflictingFile}' in '{Discipline}'",
                number.Value, availability.ConflictingFileName, discipline.ExpectedFolderName);
        }

        return new DocumentNumberAllocationDto(number, availability);
    }

    public async Task<DocumentNumberAvailabilityDto> CheckAvailabilityAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        DocumentNumberDto proposedNumber,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(discipline);
        ArgumentNullException.ThrowIfNull(convention);
        ArgumentNullException.ThrowIfNull(proposedNumber);

        var existing = await CollectExistingNumbersAsync(
            projectId, discipline, convention, accessToken, cancellationToken);

        var availability = EvaluateAvailability(existing, convention, proposedNumber);

        if (availability.IsAvailable)
        {
            _logger.LogInformation(
                "Document number {Number} is available in discipline '{Discipline}'",
                proposedNumber.Value, discipline.ExpectedFolderName);
        }
        else
        {
            _logger.LogWarning(
                "Document number {Number} is already used by '{ConflictingFile}' in discipline '{Discipline}'; suggesting {Suggestion}",
                proposedNumber.Value, availability.ConflictingFileName, discipline.ExpectedFolderName,
                availability.SuggestedNumber);
        }

        return availability;
    }

    /// <summary>
    /// Compares a proposed number against an already-collected set of existing ones.
    ///
    /// <para>
    /// Pure, and deliberately so: it is the shared comparison behind both
    /// <see cref="AllocateAndVerifyNextAsync"/> (which feeds it the one scan it made) and
    /// <see cref="CheckAvailabilityAsync"/> (which still makes its own, because its callers ask it
    /// to check a number they chose rather than one it allocated). Keeping the comparison here is
    /// what guarantees the two paths cannot disagree about whether a number is free.
    /// </para>
    /// </summary>
    private static DocumentNumberAvailabilityDto EvaluateAvailability(
        IReadOnlyList<ExistingDocumentNumber> existing,
        NamingConventionDto convention,
        DocumentNumberDto proposedNumber)
    {
        var conflict = existing.FirstOrDefault(e =>
            string.Equals(e.Number, proposedNumber.Value, StringComparison.OrdinalIgnoreCase));

        if (conflict is null)
        {
            return DocumentNumberAvailabilityDto.Available(proposedNumber.Value);
        }

        // The suggestion advances within the proposed number's own group, matching how allocation
        // scopes the sequence: the next free number for DR-ARC-S0-... is one past the highest S0,
        // unaffected by any other prefix sharing the folder.
        var group = existing
            .Where(e => string.Equals(e.GroupKey, proposedNumber.Prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var width = ResolveSequenceWidth(group, convention.Delimiter);
        var nextSequence = group.Count == 0
            ? FirstSequence
            : group.Max(e => e.Sequence ?? 0) + 1;

        var suggestedSegments = proposedNumber.Segments
            .Take(proposedNumber.Segments.Count - 1)
            .Append(proposedNumber.Segments[^1] with { Value = FormatSequence(nextSequence, width) })
            .ToList();

        var suggestion = new DocumentNumberDto(suggestedSegments, proposedNumber.Delimiter);

        return new DocumentNumberAvailabilityDto(
            ProposedNumber: proposedNumber.Value,
            IsAvailable: false,
            ConflictingFileName: conflict.FileName,
            SuggestedNumber: suggestion.Value);
    }

    /// <summary>
    /// Reads the document numbers already in use in a discipline's WIP folder.
    ///
    /// Numbers are read from each file's custom attributes rather than from its filename, because
    /// the attributes are the authoritative identity: a file renamed by hand in ACC still carries
    /// its real number in its attributes, and a filename comparison would miss the clash. The
    /// filename is used only as a fallback for files whose attributes were never populated.
    /// </summary>
    private async Task<IReadOnlyList<ExistingDocumentNumber>> CollectExistingNumbersAsync(
        string projectId,
        DisciplineFolderDto discipline,
        NamingConventionDto convention,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (discipline.Folder is null)
        {
            _logger.LogInformation(
                "Discipline '{Discipline}' has no WIP folder yet - no existing numbers to scan",
                discipline.ExpectedFolderName);
            return [];
        }

        var items = await _dataManagementService.GetFolderItemsAsync(
            projectId, discipline.Folder.Id, accessToken, cancellationToken);

        var files = items.Where(i => i.Type == ItemType.File).ToList();
        if (files.Count == 0)
        {
            return [];
        }

        var itemUrns = files
            .Select(f => f.ItemUrn)
            .Where(urn => !string.IsNullOrWhiteSpace(urn))
            .Select(urn => urn!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var attributesByUrn = new Dictionary<string, ItemCustomAttributesResultDto>(StringComparer.OrdinalIgnoreCase);
        if (itemUrns.Count > 0)
        {
            // includePathMetadata stays off: this scan only reads attribute values, and asking for
            // paths would add one HTTP request per file in the folder - the dominant cost of
            // creating a deliverable before it was made opt-in.
            var results = await _dataManagementService.GetItemCustomAttributesAsync(
                projectId, itemUrns, includePathMetadata: false, cancellationToken);

            foreach (var result in results)
            {
                attributesByUrn[result.Urn] = result;
            }
        }

        var existing = new List<ExistingDocumentNumber>();
        foreach (var file in files)
        {
            var attributes = file.ItemUrn is not null
                ? attributesByUrn.GetValueOrDefault(file.ItemUrn)
                : null;

            var number = ReadNumberFromAttributes(attributes, convention)
                         ?? ReadNumberFromFileName(file.Name, convention);

            if (number is null)
            {
                _logger.LogDebug(
                    "File '{FileName}' in '{Discipline}' carries no recognisable document number and is ignored for uniqueness",
                    file.Name, discipline.ExpectedFolderName);
                continue;
            }

            existing.Add(new ExistingDocumentNumber(
                number,
                file.Name,
                ExtractGroupKey(number, convention.Delimiter),
                ExtractSequence(number, convention.Delimiter)));
        }

        _logger.LogInformation(
            "Scanned {FileCount} file(s) in '{Discipline}', found {NumberCount} document number(s)",
            files.Count, discipline.ExpectedFolderName, existing.Count);

        return existing;
    }

    /// <summary>
    /// Composes a file's document number from its custom attribute values. Returns null when the
    /// attributes carry no value for any convention field, which means the file predates the naming
    /// standard or was created outside this workflow.
    /// </summary>
    private static string? ReadNumberFromAttributes(
        ItemCustomAttributesResultDto? attributes,
        NamingConventionDto convention)
    {
        if (attributes is null || attributes.Attributes.Count == 0)
        {
            return null;
        }

        var values = new List<string>(convention.Fields.Count);
        var populated = 0;

        foreach (var field in convention.Fields)
        {
            var value = attributes.Attributes.GetValueOrDefault(field.Name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                populated++;
            }

            values.Add(value?.Trim() ?? string.Empty);
        }

        return populated == 0 ? null : string.Join(convention.Delimiter, values);
    }

    /// <summary>
    /// Falls back to reading the number from the filename, accepted only when the name splits into
    /// exactly as many delimiter-separated segments as the convention defines. A looser rule would
    /// treat ordinary hyphenated filenames as document numbers and inflate the sequence.
    /// </summary>
    private static string? ReadNumberFromFileName(string fileName, NamingConventionDto convention)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        var parts = stem.Split(convention.Delimiter, StringSplitOptions.TrimEntries);
        return parts.Length == convention.Fields.Count ? stem : null;
    }

    /// <summary>
    /// Reads the trailing sequential segment as an integer, or null when it is not numeric.
    /// </summary>
    private static int? ExtractSequence(string number, string delimiter)
    {
        var parts = number.Split(delimiter, StringSplitOptions.TrimEntries);
        var last = parts.LastOrDefault();

        return int.TryParse(last, out var value) ? value : null;
    }

    /// <summary>
    /// Derives a number's composite key - every segment except the trailing sequential one - the
    /// same way <see cref="DocumentNumberDto.Prefix"/> does for a composed number. This is what lets
    /// scanned and reserved numbers be grouped by their shared prefix so the sequence advances per
    /// group rather than per folder. The number here is always a stem: the filename fallback has
    /// already stripped any extension, so a file type suffix can never leak into the key.
    /// </summary>
    private static string ExtractGroupKey(string number, string delimiter)
    {
        var parts = number.Split(delimiter, StringSplitOptions.TrimEntries);
        return parts.Length <= 1 ? string.Empty : string.Join(delimiter, parts.Take(parts.Length - 1));
    }

    /// <summary>
    /// Resolves the width of the sequential segment: never narrower than
    /// <see cref="MinimumSequenceWidth"/>, but widened to match a project that already uses more
    /// digits than that.
    ///
    /// Taking the maximum rather than simply adopting what exists is deliberate. Widening is safe -
    /// it keeps pace with a project using longer sequences - whereas narrowing would let a single
    /// legacy short number pull every subsequent number below the standard's required width.
    /// </summary>
    private static int ResolveSequenceWidth(IReadOnlyList<ExistingDocumentNumber> existing, string delimiter)
    {
        var widths = existing
            .Where(e => e.Sequence is not null)
            .Select(e => e.Number.Split(delimiter, StringSplitOptions.TrimEntries).LastOrDefault()?.Length ?? 0)
            .Where(w => w > 0)
            .ToList();

        return widths.Count == 0 ? MinimumSequenceWidth : Math.Max(MinimumSequenceWidth, widths.Max());
    }

    private static string FormatSequence(int sequence, int width) =>
        sequence.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');

    /// <summary>
    /// A document number already in use, with the file that carries it and the parsed pieces the
    /// allocator groups by: <paramref name="GroupKey"/> is the number minus its trailing sequential
    /// segment (its <see cref="DocumentNumberDto.Prefix"/>), and <paramref name="Sequence"/> is that
    /// trailing segment as an integer.
    /// </summary>
    private sealed record ExistingDocumentNumber(string Number, string FileName, string GroupKey, int? Sequence);
}
