namespace DigitalMIDP.Application.Exceptions;

/// <summary>
/// Exception thrown when a requested APS resource is not found.
/// </summary>
public class ResourceNotFoundException : ApplicationException
{
    public ResourceNotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} with ID '{resourceId}' not found.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public string ResourceType { get; }
    public string ResourceId { get; }
}
