namespace DigitalMIDP.Domain.Exceptions;

/// <summary>
/// Base exception for all domain-related errors.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when authentication fails or token is invalid.
/// </summary>
public sealed class AuthenticationException : DomainException
{
    public AuthenticationException(string message) : base(message)
    {
    }

    public AuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a requested resource is not found.
/// </summary>
public sealed class ResourceNotFoundException : DomainException
{
    public string ResourceType { get; }
    public string ResourceId { get; }

    public ResourceNotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} with ID '{resourceId}' was not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }
}

/// <summary>
/// Exception thrown when a domain validation fails.
/// </summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message) : base(message)
    {
    }
}
