using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace DigitalMIDP.Services;

/// <summary>
/// Resolves the company-specific Autodesk APS configuration.
///
/// <para>
/// Each company registers its own APS application and supplies its own public Client ID. The same
/// signed binary is shipped to everyone, so the Client ID is never compiled in or committed. It is
/// resolved, lowest to highest precedence, from:
/// </para>
/// <list type="number">
///   <item><c>appsettings.json</c> beside the executable (endpoint/scope defaults only).</item>
///   <item><c>%ProgramData%\DigitalMIDP\autodesk.json</c> - company-wide, written by the installer or IT.</item>
///   <item><c>%LocalAppData%\DigitalMIDP\autodesk.json</c> - per-user, written by the first-run setup window.</item>
/// </list>
/// <para>
/// The Client ID is a public PKCE identifier, not a secret; no client secret is ever stored.
/// </para>
/// </summary>
public static partial class ApsClientConfiguration
{
    /// <summary>Configuration key holding the APS Client ID.</summary>
    public const string ClientIdKey = "Autodesk:ClientId";

    /// <summary>File name used for both the company-wide and per-user configuration.</summary>
    public const string FileName = "autodesk.json";

    private const string AppFolderName = "DigitalMIDP";

    /// <summary>Company-wide configuration path (<c>%ProgramData%\DigitalMIDP\autodesk.json</c>).</summary>
    public static string CompanyConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName, FileName);

    /// <summary>Per-user configuration path (<c>%LocalAppData%\DigitalMIDP\autodesk.json</c>).</summary>
    public static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, FileName);

    /// <summary>
    /// Builds the application configuration from the base directory defaults plus the company and
    /// per-user files. All external files are optional.
    /// </summary>
    public static IConfigurationRoot Build(string baseDirectory, string companyConfigPath, string userConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userConfigPath);

        return new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile(Path.GetFullPath(companyConfigPath), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.GetFullPath(userConfigPath), optional: true, reloadOnChange: false)
            .Build();
    }

    /// <summary>Returns true when the configuration holds a well-formed Client ID.</summary>
    public static bool HasValidClientId(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return IsValidClientId(configuration[ClientIdKey]);
    }

    /// <summary>
    /// Validates the shape of an APS Client ID: 20-64 ASCII letters/digits. Does not contact APS;
    /// an unregistered but well-formed ID is only rejected at sign-in.
    /// </summary>
    public static bool IsValidClientId(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId) && ClientIdPattern().IsMatch(clientId.Trim());

    /// <summary>
    /// Writes (or updates) the Client ID in the JSON file at <paramref name="path"/>, preserving
    /// any other settings already present in that file.
    /// </summary>
    /// <exception cref="ArgumentException">The Client ID is not well-formed.</exception>
    public static void SaveClientId(string path, string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsValidClientId(clientId))
        {
            throw new ArgumentException("The Autodesk Client ID is not in a valid format.", nameof(clientId));
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        JsonObject root = new();
        if (File.Exists(fullPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(fullPath)) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                // A corrupt file is replaced rather than blocking setup.
                root = new JsonObject();
            }
        }

        if (root["Autodesk"] is not JsonObject autodesk)
        {
            autodesk = new JsonObject();
            root["Autodesk"] = autodesk;
        }

        autodesk["ClientId"] = clientId.Trim();

        var tempPath = fullPath + ".tmp";
        File.WriteAllText(tempPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, fullPath, overwrite: true);
    }

    [GeneratedRegex("^[A-Za-z0-9]{20,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ClientIdPattern();
}
