namespace DigitalMIDP.Application.Exceptions;

/// <summary>
/// Exception thrown when the authenticated user lacks permission to perform an APS operation
/// (HTTP 401/403 from the API).
/// </summary>
public class PermissionDeniedException : ApplicationException
{
    public PermissionDeniedException(string message) : base(message)
    {
    }

    public PermissionDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
