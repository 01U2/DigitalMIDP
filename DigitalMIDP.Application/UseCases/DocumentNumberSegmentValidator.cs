namespace DigitalMIDP.Application.UseCases;

/// <summary>
/// Validates the value a user supplies for one document-number segment.
///
/// A segment is not free text even when ACC does not constrain it to a list: it becomes part of a
/// delimited code AND part of a filename, so it has to survive both. A value containing the
/// delimiter silently changes how many segments the number has - which breaks the parsing that
/// duplicate detection relies on - and a value containing a space or a filesystem-reserved character
/// produces a file ACC accepts but marks as not conforming to the project's naming standard.
///
/// Catching this before creation matters more than usual here, because the file cannot be un-created:
/// by the time ACC rejects the name, a real file exists in WIP under a wrong number.
/// </summary>
public static class DocumentNumberSegmentValidator
{
    /// <summary>
    /// Characters Windows forbids in a file name. The created file is opened locally through Desktop
    /// Connector, so a name that is legal in ACC but not on disk is still a problem.
    /// </summary>
    private static readonly char[] ReservedFileNameCharacters =
        ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Returns a human-readable reason the value cannot be used, or null when it is acceptable.
    /// </summary>
    /// <param name="fieldName">The field's display name, used in the message.</param>
    /// <param name="value">The value the user entered.</param>
    /// <param name="delimiter">The naming convention's segment separator.</param>
    /// <param name="minLength">Fewest characters ACC accepts, from the project's naming standard. Null when unconstrained.</param>
    /// <param name="maxLength">Most characters ACC accepts. Null when unconstrained.</param>
    public static string? Validate(
        string fieldName,
        string? value,
        string delimiter,
        int? minLength = null,
        int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        // ACC's own per-field rules come first: they are the project's actual configuration, whereas
        // everything below is a rule this application imposes because the value also has to work as
        // part of a file name.
        if (minLength is > 0 && trimmed.Length < minLength)
        {
            return minLength == maxLength
                ? $"'{fieldName}' must be {minLength} characters."
                : $"'{fieldName}' must be at least {minLength} characters.";
        }

        if (maxLength is > 0 && trimmed.Length > maxLength)
        {
            return minLength == maxLength
                ? $"'{fieldName}' must be {maxLength} characters."
                : $"'{fieldName}' must be at most {maxLength} characters.";
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            return $"'{fieldName}' can't contain spaces. Try '{trimmed.Replace(" ", string.Empty)}'.";
        }

        if (!string.IsNullOrEmpty(delimiter) && trimmed.Contains(delimiter, StringComparison.Ordinal))
        {
            return $"'{fieldName}' can't contain '{delimiter}'.";
        }

        var reserved = trimmed.FirstOrDefault(c => ReservedFileNameCharacters.Contains(c));
        if (reserved != default)
        {
            return $"'{fieldName}' can't contain '{reserved}'.";
        }

        return null;
    }
}
