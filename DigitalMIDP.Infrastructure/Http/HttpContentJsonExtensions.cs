using System.Text.Json;
using System.Text.Json.Serialization;
using ApplicationException = DigitalMIDP.Application.Exceptions.ApplicationException;

namespace DigitalMIDP.Infrastructure.Http;

/// <summary>
/// Extension methods for HttpContent to handle JSON deserialization asynchronously.
/// </summary>
internal static class HttpContentJsonExtensions
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Deserializes the HTTP content as JSON asynchronously.
    ///
    /// <para>
    /// A body that cannot be read - empty, a literal <c>null</c>, or missing a required property -
    /// fails as <see cref="ApplicationException"/> rather than <see cref="InvalidOperationException"/>.
    /// That matters because the whole point of the typed exception hierarchy is that callers can tell
    /// a permission failure from a validation failure from a generic one; an untyped
    /// InvalidOperationException escaping from here slipped past every <c>catch</c> in the service
    /// layer and surfaced to the UI as a bare framework error with no indication of which APS call
    /// had produced it.
    /// </para>
    /// </summary>
    public static async Task<T> ReadAsAsync<T>(
        this HttpContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            using var stream = await content.ReadAsStreamAsync(cancellationToken);

            return await JsonSerializer.DeserializeAsync<T>(stream, DefaultJsonOptions, cancellationToken)
                ?? throw new ApplicationException(
                    $"APS returned a response that could not be read as {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            // The message deliberately omits the body: it carries project data, and the response is
            // logged (truncated) by the caller on its own failure path.
            throw new ApplicationException(
                $"APS returned a response that could not be read as {typeof(T).Name}: {ex.Message}", ex);
        }
    }
}
