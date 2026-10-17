using Microsoft.Extensions.Logging;
using ApplicationException = DigitalMIDP.Application.Exceptions.ApplicationException;

namespace DigitalMIDP.Infrastructure.Http;

/// <summary>
/// Validation and safe-logging helpers shared by the APS clients.
///
/// <para>
/// Two problems this exists to stop, both of which the code previously had:
/// </para>
///
/// <para>
/// <b>Unvalidated responses.</b> A deserialised response was used directly - <c>apsResponse.Links</c>
/// was dereferenced right after <c>apsResponse?.Data</c> had conceded the whole object could be
/// null, and a 200 with an empty or non-JSON body produced a null root that surfaced as a
/// <see cref="NullReferenceException"/> rather than as "APS returned an unusable response". Worse,
/// a malformed item inside an otherwise fine page - no id, no name - was mapped anyway and became a
/// row in the grid that could not be opened, edited, or matched back to anything. These helpers turn
/// the first case into a typed failure and make the second case visible and countable.
/// </para>
///
/// <para>
/// <b>Logging whole response bodies.</b> Several methods logged the complete JSON body at
/// Information. That body carries every file name, folder path, custom attribute value and user name
/// in the project, so an ordinary project load wrote the project's contents into whatever the log
/// provider happens to be - and buried the lines that actually said what went wrong. Response
/// <i>shape</i> is diagnosed with counts and status codes (<see cref="Describe"/>); bodies are only
/// ever written through <see cref="TruncateForLog"/>, at Debug, and only for a failure.
/// </para>
/// </summary>
internal static class ApsResponseGuard
{
    /// <summary>
    /// How much of a failing response body is kept. Enough for the error code and message APS puts
    /// at the start of its error envelopes, not enough to spill a whole page of project data.
    /// </summary>
    private const int MaxLoggedBodyLength = 512;

    /// <summary>
    /// Asserts a response body actually deserialised into something, converting the null that
    /// <c>JsonSerializer.Deserialize</c> returns for an empty or <c>null</c> body into a typed
    /// failure naming the operation.
    /// </summary>
    internal static T EnsureDeserialized<T>(T? deserialized, string operationDescription)
        where T : class
    {
        if (deserialized is null)
        {
            throw new ApplicationException(
                $"APS returned a response for {operationDescription} that could not be read as JSON.");
        }

        return deserialized;
    }

    /// <summary>
    /// Trims a response body down to something loggable, and says how much was dropped so a
    /// truncated body is never mistaken for a complete one.
    /// </summary>
    internal static string TruncateForLog(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "(empty)";
        }

        return body.Length <= MaxLoggedBodyLength
            ? body
            : $"{body[..MaxLoggedBodyLength]}... ({body.Length} chars total)";
    }

    /// <summary>
    /// A one-line, data-free description of a response: what came back and how big it was. This is
    /// what replaced logging the body itself - it answers "did APS return anything, and roughly how
    /// much" without reproducing the project's contents in the log.
    /// </summary>
    internal static string Describe(HttpResponseMessage response, string? body) =>
        $"HTTP {(int)response.StatusCode} {response.StatusCode}, {body?.Length ?? 0} chars";

    /// <summary>
    /// Filters a mapped collection down to the entries that are actually usable, logging a single
    /// warning naming how many were dropped and why.
    ///
    /// <para>
    /// Dropping is the right call rather than substituting a placeholder: an entry with no id cannot
    /// be fetched, edited, or correlated with anything else, so carrying it forward produces a row
    /// that looks real and fails on every interaction. Dropping it silently, however, is how data
    /// goes missing without anyone noticing - hence the warning.
    /// </para>
    /// </summary>
    internal static List<T> KeepValid<T>(
        IEnumerable<T> candidates,
        Func<T, bool> isValid,
        ILogger logger,
        string itemDescription,
        string contextDescription)
    {
        var all = candidates as IList<T> ?? candidates.ToList();
        var kept = all.Where(isValid).ToList();

        if (kept.Count != all.Count)
        {
            logger.LogWarning(
                "Discarded {DroppedCount} of {TotalCount} {ItemDescription} from {Context} - " +
                "they arrived without the identifying fields the application needs (id/name)",
                all.Count - kept.Count, all.Count, itemDescription, contextDescription);
        }

        return kept;
    }
}
