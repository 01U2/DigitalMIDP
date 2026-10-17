namespace DigitalMIDP.Infrastructure.DesktopConnector.Exceptions;

/// <summary>
/// Base exception for Desktop Connector operations.
/// </summary>
public class DesktopConnectorException : Exception
{
    public DesktopConnectorException(string message) : base(message)
    {
    }

    public DesktopConnectorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when Desktop Connector installation is not detected.
/// </summary>
public class InstallationNotDetectedException : DesktopConnectorException
{
    public InstallationNotDetectedException()
        : base("Desktop Connector installation was not detected on this system.")
    {
    }
}

/// <summary>
/// Exception thrown when a requested project is not synced in Desktop Connector.
/// </summary>
public class ProjectNotSyncedException : DesktopConnectorException
{
    public ProjectNotSyncedException(string hubName, string projectName)
        : base($"Project '{projectName}' in hub '{hubName}' is not synced with Desktop Connector.")
    {
    }
}

/// <summary>
/// Exception thrown when a requested file is not available locally.
/// </summary>
public class FileNotAvailableLocallyException : DesktopConnectorException
{
    public FileNotAvailableLocallyException(string filePath)
        : base($"File '{filePath}' is not available locally.")
    {
    }

    public FileNotAvailableLocallyException(string filePath, Exception innerException)
        : base($"File '{filePath}' could not be accessed.", innerException)
    {
    }
}
