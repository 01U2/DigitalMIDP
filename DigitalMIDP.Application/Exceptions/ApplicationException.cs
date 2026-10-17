namespace DigitalMIDP.Application.Exceptions;

/// <summary>
/// Exception thrown when an application-level operation fails.
/// </summary>
public class ApplicationException : Exception
{
    public ApplicationException(string message) : base(message)
    {
    }

    public ApplicationException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
