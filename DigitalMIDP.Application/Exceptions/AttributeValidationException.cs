namespace DigitalMIDP.Application.Exceptions;

/// <summary>
/// A single attribute that ACC rejected, identified by its attribute definition id.
/// </summary>
public sealed record AttributeValidationError(int AttributeId, string? Value, string? Detail);

/// <summary>
/// Exception thrown when the API rejects custom attribute values as invalid
/// (HTTP 400 from the API, e.g. bad option value, value that fails a constraint, or malformed date).
/// </summary>
public class AttributeValidationException : ApplicationException
{
    /// <summary>
    /// The specific attributes ACC rejected, if the API returned structured error details.
    /// Empty when only an unstructured message is available.
    /// </summary>
    public IReadOnlyList<AttributeValidationError> Errors { get; }

    public AttributeValidationException(string message, IReadOnlyList<AttributeValidationError>? errors = null)
        : base(message)
    {
        Errors = errors ?? [];
    }

    public AttributeValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }
}
