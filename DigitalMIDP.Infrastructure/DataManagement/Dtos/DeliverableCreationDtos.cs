namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

using System.Text.Json.Serialization;

/// <summary>
/// Request and response shapes for the Data Management write endpoints used to create a deliverable:
/// creating a folder, reserving a storage object, and creating an item with its first version.
///
/// These endpoints speak JSON:API rather than the plain JSON the BIM 360 Docs endpoints use, hence
/// the "jsonapi"/"data"/"relationships"/"included" envelope repeated throughout. They also require
/// the "application/vnd.api+json" content type - a plain "application/json" body is rejected.
/// </summary>
public sealed class JsonApiVersionDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}

/// <summary>
/// The "extension" discriminator every folder/item/version carries. ACC and BIM 360 projects use the
/// "autodesk.bim360" variants; other hub types use "autodesk.core". The value is derived per project
/// rather than hardcoded - see DataManagementService.Creation.
/// </summary>
public sealed class JsonApiExtensionDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}

public sealed class JsonApiResourceIdentifierDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public sealed class JsonApiRelationshipDto
{
    [JsonPropertyName("data")]
    public JsonApiResourceIdentifierDto Data { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// POST /data/v1/projects/{projectId}/folders
// ---------------------------------------------------------------------------------------------

public sealed class CreateFolderRequestDto
{
    [JsonPropertyName("jsonapi")]
    public JsonApiVersionDto JsonApi { get; set; } = new();

    [JsonPropertyName("data")]
    public CreateFolderDataDto Data { get; set; } = new();
}

public sealed class CreateFolderDataDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "folders";

    [JsonPropertyName("attributes")]
    public CreateFolderAttributesDto Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public CreateFolderRelationshipsDto Relationships { get; set; } = new();
}

public sealed class CreateFolderAttributesDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public JsonApiExtensionDto Extension { get; set; } = new();
}

public sealed class CreateFolderRelationshipsDto
{
    [JsonPropertyName("parent")]
    public JsonApiRelationshipDto Parent { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// POST /data/v1/projects/{projectId}/storage
// ---------------------------------------------------------------------------------------------

public sealed class CreateStorageRequestDto
{
    [JsonPropertyName("jsonapi")]
    public JsonApiVersionDto JsonApi { get; set; } = new();

    [JsonPropertyName("data")]
    public CreateStorageDataDto Data { get; set; } = new();
}

public sealed class CreateStorageDataDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "objects";

    [JsonPropertyName("attributes")]
    public CreateStorageAttributesDto Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public CreateStorageRelationshipsDto Relationships { get; set; } = new();
}

public sealed class CreateStorageAttributesDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class CreateStorageRelationshipsDto
{
    [JsonPropertyName("target")]
    public JsonApiRelationshipDto Target { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// POST /data/v1/projects/{projectId}/items
// ---------------------------------------------------------------------------------------------

public sealed class CreateItemRequestDto
{
    [JsonPropertyName("jsonapi")]
    public JsonApiVersionDto JsonApi { get; set; } = new();

    [JsonPropertyName("data")]
    public CreateItemDataDto Data { get; set; } = new();

    [JsonPropertyName("included")]
    public CreateItemIncludedVersionDto[] Included { get; set; } = [];
}

public sealed class CreateItemDataDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "items";

    [JsonPropertyName("attributes")]
    public CreateItemAttributesDto Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public CreateItemRelationshipsDto Relationships { get; set; } = new();
}

public sealed class CreateItemAttributesDto
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public JsonApiExtensionDto Extension { get; set; } = new();
}

public sealed class CreateItemRelationshipsDto
{
    [JsonPropertyName("tip")]
    public JsonApiRelationshipDto Tip { get; set; } = new();

    [JsonPropertyName("parent")]
    public JsonApiRelationshipDto Parent { get; set; } = new();
}

/// <summary>
/// The first version, supplied in "included" and referenced by the item's "tip" relationship via the
/// local id "1". The storage relationship points at the object the content was uploaded to.
/// </summary>
public sealed class CreateItemIncludedVersionDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "versions";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "1";

    [JsonPropertyName("attributes")]
    public CreateVersionAttributesDto Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public CreateVersionRelationshipsDto Relationships { get; set; } = new();
}

public sealed class CreateVersionAttributesDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public JsonApiExtensionDto Extension { get; set; } = new();
}

public sealed class CreateVersionRelationshipsDto
{
    [JsonPropertyName("storage")]
    public JsonApiRelationshipDto Storage { get; set; } = new();
}

// ---------------------------------------------------------------------------------------------
// OSS signed upload/download (https://developer.api.autodesk.com/oss/v2/...)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Response from GET /oss/v2/buckets/{bucket}/objects/{object}/signeds3upload.
/// The returned URLs are pre-signed S3 endpoints that must be called WITHOUT an Authorization
/// header - see the dedicated transfer HttpClient in ServiceCollectionExtensions.
/// </summary>
public sealed class SignedS3UploadResponseDto
{
    [JsonPropertyName("uploadKey")]
    public string? UploadKey { get; set; }

    [JsonPropertyName("urls")]
    public string[]? Urls { get; set; }
}

/// <summary>
/// Body for POST /oss/v2/buckets/{bucket}/objects/{object}/signeds3upload, which finalises an upload
/// after the bytes have been PUT to the pre-signed URL.
/// </summary>
public sealed class CompleteS3UploadRequestDto
{
    [JsonPropertyName("uploadKey")]
    public string UploadKey { get; set; } = string.Empty;
}

/// <summary>
/// Response from GET /oss/v2/buckets/{bucket}/objects/{object}/signeds3download.
/// </summary>
public sealed class SignedS3DownloadResponseDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
