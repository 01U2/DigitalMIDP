namespace DigitalMIDP.Tests.Fixtures;

using DigitalMIDP.Application.Dtos;
using Microsoft.Extensions.Logging;
using NSubstitute;

/// <summary>
/// Common test fixtures and helpers for all tests.
/// Provides mock objects and test data builders.
/// </summary>
public static class TestFixtures
{
    /// <summary>
    /// Create a mock logger that can be used in tests.
    /// </summary>
    public static ILogger<T> CreateMockLogger<T>()
        where T : class
    {
        return Substitute.For<ILogger<T>>();
    }

    /// <summary>
    /// Create a test hub DTO for use in tests.
    /// </summary>
    public static HubDto CreateTestHubDto(string? id = null, string? name = null)
    {
        return new HubDto
        {
            Id = id ?? "test-hub-id",
            Name = name ?? "Test Hub",
            Type = "hubs:autodesk.core:Account",
            Region = "US"
        };
    }

    /// <summary>
    /// Create a test project DTO for use in tests.
    /// </summary>
    public static ProjectDto CreateTestProjectDto(string? id = null, string? name = null)
    {
        return new ProjectDto
        {
            Id = id ?? "test-project-id",
            Name = name ?? "Test Project"
        };
    }

    /// <summary>
    /// Create a test folder DTO for use in tests.
    /// </summary>
    public static FolderDto CreateTestFolderDto(string? id = null, string? name = null)
    {
        return new FolderDto
        {
            Id = id ?? "test-folder-id",
            Name = name ?? "Test Folder"
        };
    }

    /// <summary>
    /// Create a test item DTO (file) for use in tests.
    /// </summary>
    public static ItemDto CreateTestItemDto(
        string? id = null,
        string? name = null,
        ItemType type = ItemType.File,
        long? size = null)
    {
        return new ItemDto
        {
            Id = id ?? "test-item-id",
            Name = name ?? "Test File.pdf",
            Type = type,
            Size = size ?? 1024,
            CreatedAt = System.DateTime.UtcNow.AddDays(-1),
            ModifiedAt = System.DateTime.UtcNow
        };
    }

    /// <summary>
    /// Create test authentication result DTO.
    /// </summary>
    public static AuthenticationResultDto CreateTestAuthenticationResultDto(
        string? accessToken = null,
        string? refreshToken = null,
        int expiresInSeconds = 3600)
    {
        return new AuthenticationResultDto
        {
            AccessToken = accessToken ?? "test-access-token-" + System.Guid.NewGuid(),
            RefreshToken = refreshToken ?? "test-refresh-token-" + System.Guid.NewGuid(),
            ExpiresInSeconds = expiresInSeconds
        };
    }
}
