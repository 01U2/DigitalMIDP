namespace DigitalMIDP.Application.Exceptions;

/// <summary>
/// Exception thrown when a service call fails due to an invalid or expired token.
/// </summary>
public class InvalidTokenException : ApplicationException
{
    public InvalidTokenException(string message) : base(message)
    {
    }

    public InvalidTokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
