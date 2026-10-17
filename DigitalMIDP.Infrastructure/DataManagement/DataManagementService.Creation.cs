using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Infrastructure.DataManagement.Dtos;
using DigitalMIDP.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApplicationException = DigitalMIDP.Application.Exceptions.ApplicationException;

namespace DigitalMIDP.Infrastructure.DataManagement;

/// <summary>
/// The write half of <see cref="DataManagementService"/>: creating folders, and creating a file by
/// copying a template's content into a new item.
///
/// Kept in its own partial so the read paths - which are large, delicate, and heavily documented in
/// docs/edit-custom-attributes.md - stay legible.
///
/// <para>
/// Creating a file in ACC is a four-call sequence, not a single upload:
/// <list type="number">
///   <item>reserve a storage object in the destination folder (POST .../storage),</item>
///   <item>ask OSS for a pre-signed S3 URL and PUT the bytes to it,</item>
///   <item>tell OSS the upload is complete,</item>
///   <item>create the item plus its first version, pointing at that storage object.</item>
/// </list>
/// There is no "copy template" API, so creating from a template means downloading the template's
/// bytes and running them back through this same sequence under a new name.
/// </para>
/// </summary>
internal sealed partial class DataManagementService
{
    /// <summary>
    /// HttpClient name for pre-signed S3 transfers. These must NOT carry an Authorization header:
    /// the signature is in the URL, and S3 rejects a request that also presents a bearer token. The
    /// class's main HttpClient runs every request through <c>ApsAuthorizationHandler</c>, so signed
    /// transfers have to go out through a separate, unauthenticated client.
    /// </summary>
    internal const string TransferHttpClientName = "ApsSignedTransfer";

    private const string JsonApiContentType = "application/vnd.api+json";

    /// <summary>
    /// Extension discriminators for a BIM 360 / ACC hub. Projects on other hub types use the
    /// "autodesk.core" equivalents, but this application targets ACC exclusively.
    /// </summary>
    private const string FolderExtensionType = "folders:autodesk.bim360:Folder";
    private const string ItemExtensionType = "items:autodesk.bim360:File";
    private const string VersionExtensionType = "versions:autodesk.bim360:File";

    /// <inheritdoc />
    public async Task<FolderDto> CreateFolderAsync(
        string projectId,
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentFolderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);

        _logger.LogInformation(
            "Creating folder '{FolderName}' under {ParentFolderId} in project {ProjectId}",
            folderName, parentFolderId, projectId);

        var request = new CreateFolderRequestDto
        {
            Data = new CreateFolderDataDto
            {
                Attributes = new CreateFolderAttributesDto
                {
                    Name = folderName,
                    // The resource discriminator, NOT the request's content type - the two are
                    // unrelated despite both being "types".
                    Extension = new JsonApiExtensionDto { Type = FolderExtensionType }
                },
                Relationships = new CreateFolderRelationshipsDto
                {
                    Parent = new JsonApiRelationshipDto
                    {
                        Data = new JsonApiResourceIdentifierDto { Type = "folders", Id = parentFolderId }
                    }
                }
            }
        };

        var url = $"{DataApiBaseUrl}/projects/{projectId}/folders";
        var responseJson = await SendJsonApiAsync(url, request, $"create folder '{folderName}'", cancellationToken);

        using var document = JsonDocument.Parse(responseJson);
        var folderId = document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new ApplicationException(
                $"ACC accepted the folder creation for '{folderName}' but returned no folder id.");
        }

        _logger.LogInformation("Created folder '{FolderName}' with id {FolderId}", folderName, folderId);

        return new FolderDto
        {
            Id = folderId,
            Name = folderName,
            ParentFolderId = parentFolderId
        };
    }

    /// <inheritdoc />
    public async Task<ItemDto> CreateItemFromTemplateAsync(
        string projectId,
        string targetFolderId,
        string templateItemId,
        string newFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFileName);

        _logger.LogInformation(
            "Creating '{FileName}' in folder {FolderId} from template {TemplateItemId} (project {ProjectId})",
            newFileName, targetFolderId, templateItemId, projectId);

        var content = await DownloadItemContentAsync(projectId, templateItemId, cancellationToken);

        _logger.LogInformation(
            "Downloaded {ByteCount} bytes from template {TemplateItemId}", content.Length, templateItemId);

        return await CreateItemAsync(projectId, targetFolderId, newFileName, content, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ItemDto> CreateItemAsync(
        string projectId,
        string targetFolderId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        // 1. Reserve a storage object in the destination folder.
        var storageUrn = await CreateStorageAsync(projectId, targetFolderId, fileName, cancellationToken);

        // 2-3. Upload the bytes into it.
        await UploadToStorageAsync(storageUrn, content, cancellationToken);

        // 4. Create the item and its first version, pointing at the populated storage object.
        var request = new CreateItemRequestDto
        {
            Data = new CreateItemDataDto
            {
                Attributes = new CreateItemAttributesDto
                {
                    DisplayName = fileName,
                    Extension = new JsonApiExtensionDto { Type = ItemExtensionType }
                },
                Relationships = new CreateItemRelationshipsDto
                {
                    // "1" is the local id of the version supplied in "included" below - JSON:API
                    // lets the item reference a resource being created in the same request.
                    Tip = new JsonApiRelationshipDto
                    {
                        Data = new JsonApiResourceIdentifierDto { Type = "versions", Id = "1" }
                    },
                    Parent = new JsonApiRelationshipDto
                    {
                        Data = new JsonApiResourceIdentifierDto { Type = "folders", Id = targetFolderId }
                    }
                }
            },
            Included =
            [
                new CreateItemIncludedVersionDto
                {
                    Attributes = new CreateVersionAttributesDto
                    {
                        Name = fileName,
                        Extension = new JsonApiExtensionDto { Type = VersionExtensionType }
                    },
                    Relationships = new CreateVersionRelationshipsDto
                    {
                        Storage = new JsonApiRelationshipDto
                        {
                            Data = new JsonApiResourceIdentifierDto { Type = "objects", Id = storageUrn }
                        }
                    }
                }
            ]
        };

        var url = $"{DataApiBaseUrl}/projects/{projectId}/items";
        var responseJson = await SendJsonApiAsync(url, request, $"create item '{fileName}'", cancellationToken);

        var (itemId, versionUrn) = ParseCreatedItem(responseJson);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new ApplicationException(
                $"ACC accepted the creation of '{fileName}' but returned no item id.");
        }

        if (string.IsNullOrWhiteSpace(versionUrn))
        {
            // The file exists, but without the version urn its custom attributes cannot be written -
            // and a deliverable with no metadata is not a valid deliverable. Surfacing this rather
            // than returning a half-created file lets the caller tell the user precisely what state
            // the project is in.
            //
            // This is the one case where the create-item body earns a place in the log: the urn is
            // read out of it, so when creation "half works" the body is the evidence. It used to be
            // logged at Information on EVERY successful creation, which meant a batch of 20 files
            // wrote 20 full JSON:API envelopes into the log to no purpose.
            _logger.LogError(
                "Item '{FileName}' was created (id {ItemId}) but ACC returned no version urn - attributes cannot be applied. Response: {Body}",
                fileName, itemId, ApsResponseGuard.TruncateForLog(responseJson));
        }

        _logger.LogInformation(
            "Created item '{FileName}': itemId={ItemId}, versionUrn={VersionUrn}",
            fileName, itemId, versionUrn);

        return new ItemDto
        {
            Id = itemId,
            Name = fileName,
            Type = ItemType.File,
            ParentId = targetFolderId,
            ItemUrn = itemId,
            TipVersionUrn = versionUrn,
            Size = content.Length,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadItemContentAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var storageUrn = await GetTipVersionStorageUrnAsync(projectId, itemId, cancellationToken);
        var (bucketKey, objectKey) = ParseStorageUrn(storageUrn);

        // Ask OSS for a pre-signed download URL rather than streaming through the API - this is the
        // supported path for objects of any size and avoids proxying the whole file through Autodesk.
        var signedUrl = $"{OssApiBaseUrl}/buckets/{bucketKey}/objects/{Uri.EscapeDataString(objectKey)}/signeds3download";
        using var signedRequest = new HttpRequestMessage(HttpMethod.Get, signedUrl);

        _logger.LogDebug("Request: GET {Url}", signedUrl);
        var signedResponse = await _httpClient.SendAsync(signedRequest, cancellationToken);
        var signedJson = await signedResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!signedResponse.IsSuccessStatusCode)
        {
            throw BuildFailureException(
                signedResponse.StatusCode, $"get a download URL for template item {itemId}", signedJson);
        }

        var signed = JsonSerializer.Deserialize<SignedS3DownloadResponseDto>(
            signedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (string.IsNullOrWhiteSpace(signed?.Url))
        {
            throw new ApplicationException(
                $"OSS returned no download URL for template item {itemId} (status '{signed?.Status}').");
        }

        var transferClient = _httpClientFactory.CreateClient(TransferHttpClientName);
        using var downloadResponse = await transferClient.GetAsync(
            signed.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!downloadResponse.IsSuccessStatusCode)
        {
            throw new ApplicationException(
                $"Failed to download template content (HTTP {(int)downloadResponse.StatusCode}).");
        }

        return await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Reserves an OSS storage object for a file about to be created in the given folder.
    /// POST /data/v1/projects/{projectId}/storage
    /// </summary>
    private async Task<string> CreateStorageAsync(
        string projectId,
        string targetFolderId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var request = new CreateStorageRequestDto
        {
            Data = new CreateStorageDataDto
            {
                Attributes = new CreateStorageAttributesDto { Name = fileName },
                Relationships = new CreateStorageRelationshipsDto
                {
                    Target = new JsonApiRelationshipDto
                    {
                        Data = new JsonApiResourceIdentifierDto { Type = "folders", Id = targetFolderId }
                    }
                }
            }
        };

        var url = $"{DataApiBaseUrl}/projects/{projectId}/storage";
        var responseJson = await SendJsonApiAsync(url, request, $"reserve storage for '{fileName}'", cancellationToken);

        using var document = JsonDocument.Parse(responseJson);
        var storageUrn = document.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(storageUrn))
        {
            throw new ApplicationException($"ACC returned no storage id when reserving storage for '{fileName}'.");
        }

        _logger.LogDebug("Reserved storage {StorageUrn} for '{FileName}'", storageUrn, fileName);
        return storageUrn;
    }

    /// <summary>
    /// Uploads content into a reserved storage object using the OSS signed-S3 flow: request a
    /// pre-signed URL, PUT the bytes straight to S3, then tell OSS the upload is complete.
    /// </summary>
    private async Task UploadToStorageAsync(string storageUrn, byte[] content, CancellationToken cancellationToken)
    {
        var (bucketKey, objectKey) = ParseStorageUrn(storageUrn);
        var encodedObjectKey = Uri.EscapeDataString(objectKey);
        var signedUploadUrl = $"{OssApiBaseUrl}/buckets/{bucketKey}/objects/{encodedObjectKey}/signeds3upload";

        // 1. Obtain the pre-signed URL.
        using var signedRequest = new HttpRequestMessage(HttpMethod.Get, signedUploadUrl);
        _logger.LogDebug("Request: GET {Url}", signedUploadUrl);
        var signedResponse = await _httpClient.SendAsync(signedRequest, cancellationToken);
        var signedJson = await signedResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!signedResponse.IsSuccessStatusCode)
        {
            throw BuildFailureException(signedResponse.StatusCode, "get an upload URL", signedJson);
        }

        var signed = JsonSerializer.Deserialize<SignedS3UploadResponseDto>(
            signedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var uploadUrl = signed?.Urls?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(uploadUrl) || string.IsNullOrWhiteSpace(signed?.UploadKey))
        {
            throw new ApplicationException("OSS returned no pre-signed upload URL for the new file.");
        }

        // 2. PUT the bytes. This goes to S3 directly, so it must not carry the APS bearer token.
        var transferClient = _httpClientFactory.CreateClient(TransferHttpClientName);
        using var uploadContent = new ByteArrayContent(content);
        using var uploadResponse = await transferClient.PutAsync(uploadUrl, uploadContent, cancellationToken);

        if (!uploadResponse.IsSuccessStatusCode)
        {
            var body = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "S3 upload failed with status {StatusCode}: {Body}", (int)uploadResponse.StatusCode, body);
            throw new ApplicationException(
                $"Failed to upload file content (HTTP {(int)uploadResponse.StatusCode}).");
        }

        // 3. Finalise, which is what actually makes the object readable.
        using var completeRequest = new HttpRequestMessage(HttpMethod.Post, signedUploadUrl)
        {
            Content = JsonContent.Create(new CompleteS3UploadRequestDto { UploadKey = signed.UploadKey })
        };

        _logger.LogDebug("Request: POST {Url} (complete upload)", signedUploadUrl);
        var completeResponse = await _httpClient.SendAsync(completeRequest, cancellationToken);
        var completeJson = await completeResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!completeResponse.IsSuccessStatusCode)
        {
            throw BuildFailureException(completeResponse.StatusCode, "finalise the upload", completeJson);
        }

        _logger.LogInformation("Uploaded {ByteCount} bytes to storage {StorageUrn}", content.Length, storageUrn);
    }

    /// <summary>
    /// Resolves the OSS storage object backing an item's tip (latest) version.
    /// GET /data/v1/projects/{projectId}/items/{itemId}/tip
    /// </summary>
    private async Task<string> GetTipVersionStorageUrnAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken)
    {
        var url = $"{DataApiBaseUrl}/projects/{projectId}/items/{Uri.EscapeDataString(itemId)}/tip";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        _logger.LogDebug("Request: GET {Url}", url);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildFailureException(response.StatusCode, $"read template item {itemId}", json);
        }

        using var document = JsonDocument.Parse(json);
        var storageUrn = document.RootElement
            .TryGetPath("data", "relationships", "storage", "data", "id")?
            .GetString();

        if (string.IsNullOrWhiteSpace(storageUrn))
        {
            throw new ApplicationException(
                $"Template item {itemId} has no storage object - it may be a placeholder with no uploaded content.");
        }

        return storageUrn;
    }

    /// <summary>
    /// Splits an OSS object urn ("urn:adsk.objects:os.object:{bucketKey}/{objectKey}") into its
    /// bucket and object parts, which the OSS endpoints take as separate path segments.
    /// </summary>
    private static (string BucketKey, string ObjectKey) ParseStorageUrn(string storageUrn)
    {
        const string prefix = "urn:adsk.objects:os.object:";

        var withoutPrefix = storageUrn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? storageUrn[prefix.Length..]
            : storageUrn;

        var separatorIndex = withoutPrefix.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == withoutPrefix.Length - 1)
        {
            throw new ApplicationException($"Unrecognised storage urn format: '{storageUrn}'.");
        }

        return (withoutPrefix[..separatorIndex], withoutPrefix[(separatorIndex + 1)..]);
    }

    /// <summary>
    /// Reads the created item's id and the urn of its first version from the create-item response.
    /// The version arrives in "included" rather than "data", since the response mirrors the request's
    /// JSON:API envelope.
    /// </summary>
    private static (string? ItemId, string? VersionUrn) ParseCreatedItem(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        var itemId = root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

        string? versionUrn = null;
        if (root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
        {
            versionUrn = included.EnumerateArray()
                .Where(e => e.TryGetProperty("type", out var type) &&
                            string.Equals(type.GetString(), "versions", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.TryGetProperty("id", out var vid) ? vid.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        // Fall back to the item's tip relationship. Both places carry the same urn, but which one is
        // populated has varied, and without it the file's attributes cannot be written at all - so
        // it is worth reading whichever source actually has it.
        if (string.IsNullOrWhiteSpace(versionUrn))
        {
            versionUrn = root.TryGetPath("data", "relationships", "tip", "data", "id")?.GetString();
        }

        // A local id echoed back from the request ("1") is not a real urn - treat it as absent so the
        // caller reports the gap rather than sending "1" to the attributes endpoint.
        if (versionUrn is not null && !versionUrn.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            versionUrn = null;
        }

        return (itemId, versionUrn);
    }

    /// <summary>
    /// Sends a JSON:API request and returns the raw response body, translating failures into the
    /// typed exception hierarchy so callers can distinguish a permissions problem (very likely here -
    /// creating files needs both the <c>data:create</c> scope and ACC folder permissions) from a
    /// validation problem or a generic failure.
    /// </summary>
    private async Task<string> SendJsonApiAsync<TRequest>(
        string url,
        TRequest body,
        string operationDescription,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(body);

        // The Content-Type must be exactly "application/vnd.api+json" with NO media type
        // parameters. The JSON:API specification these endpoints implement requires a server to
        // reject a parameterised JSON:API content type with 415 Unsupported Media Type, and APS
        // enforces that strictly. The obvious `new StringContent(payload, Encoding.UTF8, type)`
        // appends "; charset=utf-8" and is refused for that reason alone, with a body that is
        // otherwise perfectly valid - so the header is built explicitly here instead.
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
        content.Headers.ContentType = new MediaTypeHeaderValue(JsonApiContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonApiContentType));

        _logger.LogDebug(
            "Request: POST {Url} ({Operation}), Content-Type: {ContentType}",
            url, operationDescription, content.Headers.ContentType);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Both bodies are kept on the failure path, truncated: a rejected JSON:API call is
            // almost always a header or envelope problem, and the response alone rarely says which.
            // Neither is logged when the call succeeds.
            _logger.LogWarning(
                "Failed to {Operation}: {Status}. Response: {Body} (sent Content-Type: {ContentType}, request: {RequestBody})",
                operationDescription,
                ApsResponseGuard.Describe(response, responseJson),
                ApsResponseGuard.TruncateForLog(responseJson),
                content.Headers.ContentType,
                ApsResponseGuard.TruncateForLog(payload));

            throw BuildFailureException(response.StatusCode, operationDescription, responseJson);
        }

        _logger.LogDebug(
            "Completed {Operation}: {Status}",
            operationDescription, ApsResponseGuard.Describe(response, responseJson));

        return responseJson;
    }

    /// <summary>
    /// Maps an HTTP failure onto the typed exception hierarchy, following the established
    /// 401/403 -&gt; permission, 400 -&gt; validation, otherwise generic pattern used by
    /// UpdateItemCustomAttributesAsync.
    /// </summary>
    private static ApplicationException BuildFailureException(
        HttpStatusCode statusCode,
        string operationDescription,
        string responseBody)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new Application.Exceptions.PermissionDeniedException(
                $"You don't have permission to {operationDescription}. Creating files requires the " +
                "'data:create' scope and write access to the destination folder in ACC.");
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            return new Application.Exceptions.AttributeValidationException(
                $"ACC rejected the request to {operationDescription}: {responseBody}");
        }

        if (statusCode == HttpStatusCode.Conflict)
        {
            return new ApplicationException(
                $"Unable to {operationDescription} - a conflicting item already exists in ACC.");
        }

        if (statusCode == HttpStatusCode.UnsupportedMediaType)
        {
            // Almost always the Content-Type rather than the body: these endpoints reject
            // "application/vnd.api+json" carrying any media type parameter, charset included.
            return new ApplicationException(
                $"Unable to {operationDescription} - ACC rejected the request's content type. " +
                $"It must be exactly '{JsonApiContentType}' with no parameters (no charset).");
        }

        return new ApplicationException(
            $"Unable to {operationDescription} (HTTP {(int)statusCode}): {responseBody}");
    }
}

/// <summary>
/// Small helper for walking nested JSON without a chain of TryGetProperty calls at each level.
/// </summary>
internal static class JsonElementExtensions
{
    internal static JsonElement? TryGetPath(this JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
