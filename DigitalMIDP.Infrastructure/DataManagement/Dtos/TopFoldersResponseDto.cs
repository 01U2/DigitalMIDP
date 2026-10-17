namespace DigitalMIDP.Infrastructure.DataManagement.Dtos;

using System.Text.Json.Serialization;

public sealed class TopFoldersResponseDto
{
    [JsonPropertyName("data")]
    public TopFolderDto[]? Data { get; set; }
}

public sealed class TopFolderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public TopFolderAttributesDto? Attributes { get; set; }
}

public sealed class TopFolderAttributesDto
{
    [JsonPropertyName("extension")]
    public TopFolderExtensionDto? Extension { get; set; }
}

public sealed class TopFolderExtensionDto
{
    [JsonPropertyName("data")]
    public TopFolderDataDto? Data { get; set; }
}

public sealed class TopFolderDataDto
{
    [JsonPropertyName("namingStandardIds")]
    public string[]? NamingStandardIds { get; set; }
}
