using FluentAssertions;
using DigitalMIDP.Application.Dtos;
using DigitalMIDP.Infrastructure.Caching;
using DigitalMIDP.Infrastructure.DataManagement;
using DigitalMIDP.Infrastructure.DataManagement.Dtos;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Net;
using System.Text.Json;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.DataManagement;

/// <summary>
/// Unit tests for DataManagementService, focused on custom attributes batch-get functionality.
/// </summary>
public sealed class DataManagementServiceTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataManagementService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpMessageHandlerMock _handlerMock;

    public DataManagementServiceTests()
    {
        _handlerMock = new HttpMessageHandlerMock();
        _httpClient = new HttpClient(_handlerMock);
        _logger = Substitute.For<ILogger<DataManagementService>>();
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
    }

    /// <summary>
    /// Builds a service with a real, empty <see cref="ApsMetadataCache"/>.
    ///
    /// <para>
    /// A real cache rather than a substitute, so these tests exercise the caching path the
    /// application actually runs - including that a cached read does NOT reach the HTTP handler,
    /// which is what several of the request-count assertions below depend on. A fresh one per call
    /// keeps each service instance independent, so a test cannot be affected by what an earlier one
    /// happened to fetch.
    /// </para>
    /// </summary>
    private DataManagementService CreateService() => new(
        _httpClient,
        _httpClientFactory,
        new ApsMetadataCache(Substitute.For<ILogger<ApsMetadataCache>>()),
        _logger);

    #region Pagination Tests

    [Fact]
    public async Task GetHubsAsync_WithMultiplePages_ReturnsAggregatedResults()
    {
        // Arrange
        var service = CreateService();
        var hubPage1 = new ApsHubsResponse
        {
            Data = new[]
            {
                new ApsHubDto
                {
                    Id = "a.hub1",
                    Attributes = new ApsHubDto.HubAttributes
                    {
                        Name = "Hub One",
                        Region = "US"
                    }
                }
            },
            Links = new PaginationLinks
            {
                Next = new PaginationLink
                {
                    Href = "https://developer.api.autodesk.com/project/v1/hubs?page[limit]=200&page[number]=2"
                }
            }
        };

        var hubPage2 = new ApsHubsResponse
        {
            Data = new[]
            {
                new ApsHubDto
                {
                    Id = "b.hub2",
                    Attributes = new ApsHubDto.HubAttributes
                    {
                        Name = "Hub Two",
                        Region = "EMEA"
                    }
                }
            },
            Links = null
        };

        _handlerMock.OnRequest = request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            return url.Contains("page[number]=2", StringComparison.OrdinalIgnoreCase)
                ? CreateJsonResponse(hubPage2)
                : CreateJsonResponse(hubPage1);
        };

        // Act
        var result = await service.GetHubsAsync("token", CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new[]
        {
            new HubDto
            {
                Id = "a.hub1",
                Name = "Hub One",
                Type = "ACC",
                Region = "US"
            },
            new HubDto
            {
                Id = "b.hub2",
                Name = "Hub Two",
                Type = "BIM360",
                Region = "EMEA"
            }
        });
    }

    [Fact]
    public async Task GetProjectsAsync_WithEmptyPage_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var response = new ApsProjectsResponse
        {
            Data = Array.Empty<ApsProjectDto>(),
            Links = null
        };

        _handlerMock.OnRequest = _ => CreateJsonResponse(response);

        // Act
        var result = await service.GetProjectsAsync("hub123", "token", CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFolderItemsAsync_WithMultiplePages_ReturnsAggregatedResults()
    {
        // Arrange
        var service = CreateService();
        var createdAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var modifiedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var page1 = new ApsFolderContentsResponse
        {
            Data = new[]
            {
                new ApsItemDto
                {
                    Id = "folder-1",
                    Type = "items:autodesk.core:Folder",
                    Attributes = new ApsItemDto.ItemAttributes
                    {
                        Name = "Folder One",
                        DisplayName = "Folder One",
                        ParentId = "parent-1",
                        CreateTime = createdAt,
                        LastModifiedTime = modifiedAt
                    }
                }
            },
            Links = new PaginationLinks
            {
                Next = new PaginationLink
                {
                    Href = "https://developer.api.autodesk.com/data/v1/projects/project123/folders/folder123/contents?page[limit]=200&page[number]=2"
                }
            }
        };

        var page2 = new ApsFolderContentsResponse
        {
            Data = new[]
            {
                new ApsItemDto
                {
                    Id = "urn:adsk.wipprod:dm.lineage:itemId",
                    Type = "items:autodesk.core:File",
                    Attributes = new ApsItemDto.ItemAttributes
                    {
                        Name = "File One",
                        DisplayName = "File One",
                        ParentId = "parent-1",
                        CreateTime = createdAt,
                        LastModifiedTime = modifiedAt,
                        StorageSize = 1024
                    },
                    Relationships = new ApsItemDto.ItemRelationships
                    {
                        Tip = new ApsItemDto.ItemRelationshipData
                        {
                            Data = new ApsItemDto.ItemRelationshipResource
                            {
                                Id = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId"
                            }
                        }
                    }
                }
            },
            Links = null
        };

        _handlerMock.OnRequest = request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            return url.Contains("page[number]=2", StringComparison.OrdinalIgnoreCase)
                ? CreateJsonResponse(page2)
                : CreateJsonResponse(page1);
        };

        // Act
        var result = await service.GetFolderItemsAsync("project123", "folder123", "token", CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new[]
        {
            new ItemDto
            {
                Id = "folder-1",
                Name = "Folder One",
                Type = ItemType.Folder,
                ParentId = "parent-1",
                CreatedAt = createdAt,
                ModifiedAt = modifiedAt
            },
            new ItemDto
            {
                Id = "urn:adsk.wipprod:dm.lineage:itemId",
                Name = "File One",
                Type = ItemType.File,
                ParentId = "parent-1",
                ItemUrn = "urn:adsk.wipprod:dm.lineage:itemId",
                Size = 1024,
                CreatedAt = createdAt,
                ModifiedAt = modifiedAt
            }
        });
    }

    [Fact]
    public async Task GetFolderItemsAsync_WithEmptyPage_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var response = new ApsFolderContentsResponse
        {
            Data = Array.Empty<ApsItemDto>(),
            Links = null
        };

        _handlerMock.OnRequest = _ => CreateJsonResponse(response);

        // Act
        var result = await service.GetFolderItemsAsync("project123", "folder123", "token", CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetItemCustomAttributesAsync Tests

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithValidVersionUrns_ReturnsMappedResults()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrns = new[] { "urn:adsk.bim360:dm.item:itemId:dm.version:versionId1" };
        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = itemUrns[0],
                    ItemUrn = itemUrns[0],
                    Name = "TestFile.rvt",
                    Title = "TestFile.rvt",
                    LastModifiedTime = "2026-02-21T22:41:48+0000",
                    LastModifiedUserName = "Tester",
                    RevisionNumber = 2,
                    ApprovalStatus = new ApprovalStatusDto { Label = "Approved", Value = "approved" },
                    CustomAttributes = new[]
                    {
                        new CustomAttributeDto { Name = "Category", Value = "Structural" },
                        new CustomAttributeDto { Name = "Status", Value = "Draft" }
                    }
                }
            },
            Errors = []
        };

        _handlerMock.OnRequest = request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (url.Contains("/items/", StringComparison.OrdinalIgnoreCase))
            {
                var itemResponse = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            pathInProject = new[] { "Project Files", "Folder", "TestFile.rvt" }
                        },
                        links = new
                        {
                            webView = new
                            {
                                href = "https://example.test/webview"
                            }
                        }
                    }
                };

                return CreateJsonResponse(itemResponse);
            }

            return CreateJsonResponse(mockResponse);
        };

        // Act
        // includePathMetadata: true - the two path fields asserted below are opt-in, because
        // populating them costs one extra HTTP request per item and no production caller reads them
        // from here. This test is what keeps that opt-in path working.
        var result = await service.GetItemCustomAttributesAsync(
            projectId, itemUrns, includePathMetadata: true);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(1);
        resultList[0].Urn.Should().Be(itemUrns[0]);
        resultList[0].Name.Should().Be("TestFile.rvt");
        resultList[0].Title.Should().Be("TestFile.rvt");
        resultList[0].LastModifiedTime.Should().Be("2026-02-21T22:41:48+0000");
        resultList[0].LastModifiedUserName.Should().Be("Tester");
        resultList[0].RevisionNumber.Should().Be(2);
        resultList[0].ApprovalStatus.Should().Be("approved");
        resultList[0].PathInProject.Should().Be("Project Files/Folder/TestFile.rvt");
        resultList[0].WebViewLink.Should().Be("https://example.test/webview");
        resultList[0].Attributes.Should().ContainKey("Category").WhoseValue.Should().Be("Structural");
        resultList[0].Attributes.Should().ContainKey("Status").WhoseValue.Should().Be("Draft");
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithEmptyUrns_ReturnsEmpty()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrns = Array.Empty<string>();
        var service = CreateService();

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, itemUrns);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithNullUrns_ThrowsArgumentNullException()
    {
        // Arrange
        var projectId = "b.projectId123";
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.GetItemCustomAttributesAsync(projectId, null!));
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithInvalidProjectId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var itemUrns = new[] { "urn:adsk.bim360:dm.item:itemId:dm.version:versionId1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetItemCustomAttributesAsync("", itemUrns));
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithDuplicateUrns_DeduplicatesBeforeBatch()
    {
        // Arrange
        var projectId = "b.projectId123";
        var validUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var itemUrns = new[] { validUrn, validUrn, validUrn }; // Same URN 3 times

        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = validUrn,
                    // itemUrn is what a result is matched back to its file by, so a fixture that
                    // omits it no longer produces a usable row - see
                    // GetItemCustomAttributesAsync_WithResultMissingItemUrn_DropsIt.
                    ItemUrn = validUrn,
                    Name = "File.rvt",
                    CustomAttributes = []
                }
            },
            Errors = []
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, itemUrns);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithResponseErrors_LogsWarningButReturnsBatchResults()
    {
        // Arrange
        var projectId = "b.projectId123";
        var validUrns = new[]
        {
            "urn:adsk.bim360:dm.item:itemId1:dm.version:versionId1",
            "urn:adsk.bim360:dm.item:itemId2:dm.version:versionId2"
        };

        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = validUrns[0],
                    ItemUrn = validUrns[0],
                    Name = "File1.rvt",
                    CustomAttributes = new[] { new CustomAttributeDto { Name = "Test", Value = "Value" } }
                }
            },
            Errors = new[]
            {
                new BatchGetVersionErrorDto
                {
                    ItemUrn = validUrns[1],
                    ErrorCode = "ITEM_NOT_FOUND",
                    Message = "Item not found"
                }
            }
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, validUrns);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(1);
        resultList[0].Urn.Should().Be(validUrns[0]);
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithDuplicateAttributeNames_KeepsLastValue()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var itemUrns = new[] { itemUrn };

        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = itemUrn,
                    // itemUrn is what a result is matched back to its file by, so a fixture that
                    // omits it no longer produces a usable row - see
                    // GetItemCustomAttributesAsync_WithResultMissingItemUrn_DropsIt.
                    ItemUrn = itemUrn,
                    Name = "File.rvt",
                    CustomAttributes = new[]
                    {
                        new CustomAttributeDto { Name = "Category", Value = "FirstValue" },
                        new CustomAttributeDto { Name = "Category", Value = "SecondValue" }
                    }
                }
            },
            Errors = []
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, itemUrns);

        // Assert
        var resultList = result.ToList();
        resultList[0].Attributes["Category"].Should().Be("SecondValue");
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithNullAttributes_ReturnsEmptyDictionary()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var itemUrns = new[] { itemUrn };

        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = itemUrn,
                    ItemUrn = itemUrn,
                    Name = "File.rvt",
                    CustomAttributes = null
                }

            },
            Errors = []
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, itemUrns);

        // Assert
        var resultList = result.ToList();
        resultList[0].Attributes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithHttpError_ThrowsApplicationException()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrns = new[] { "urn:adsk.bim360:dm.item:itemId:dm.version:versionId" };
        var service = CreateService();

        _handlerMock.SetupResponse<object>(HttpStatusCode.Unauthorized, null);

        // Act & Assert
        await Assert.ThrowsAsync<global::DigitalMIDP.Application.Exceptions.ApplicationException>(
            () => service.GetItemCustomAttributesAsync(projectId, itemUrns));
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_ConvertsProjectIdFormat()
    {
        // Arrange
        var projectIdWithPrefix = "b.projectId123";
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var itemUrns = new[] { itemUrn };

        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = itemUrn,
                    ItemUrn = itemUrn,
                    Name = "File.rvt",
                    CustomAttributes = []
                }
            },
            Errors = []
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectIdWithPrefix, itemUrns);

        // Assert
        result.Should().NotBeEmpty();
        // Verify the URL was called with converted project ID (without "b." prefix)
        _handlerMock.LastRequestUrl.Should().Contain("projectId123");
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_UsesRawUrnInItemPathRequest()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrn = "urn:adsk.bim360:dm.item:itemId:dm.version:versionId";
        var itemUrns = new[] { itemUrn };
        var service = CreateService();
        var requestUrls = new List<string>();

        _handlerMock.OnRequest = request =>
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            requestUrls.Add(url);

            if (url.Contains("/items/", StringComparison.OrdinalIgnoreCase))
            {
                var itemResponse = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            pathInProject = new[] { "Project Files", "Folder", "TestFile.rvt" }
                        }
                    }
                };

                return CreateJsonResponse(itemResponse);
            }

            var mockResponse = new BatchGetVersionsResponseDto
            {
                Results = new[]
                {
                    new BatchGetVersionResultDto
                    {
                        Urn = itemUrn,
                        Name = "File.rvt",
                        CustomAttributes = []
                    }
                },
                Errors = []
            };

            return CreateJsonResponse(mockResponse);
        };

        // Act
        // Needs includePathMetadata: true - the per-item path request this asserts on is exactly
        // what the flag controls.
        await service.GetItemCustomAttributesAsync(projectId, itemUrns, includePathMetadata: true);

        // Assert
        requestUrls.Should().Contain(url => url.Contains($"/items/{itemUrn}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithoutPathMetadata_IssuesNoPerItemRequests()
    {
        // Arrange
        var projectId = "b.projectId123";
        var itemUrns = new[]
        {
            "urn:adsk.bim360:dm.item:itemId1:dm.version:versionId1",
            "urn:adsk.bim360:dm.item:itemId2:dm.version:versionId2",
            "urn:adsk.bim360:dm.item:itemId3:dm.version:versionId3"
        };

        var service = CreateService();
        var requestUrls = new List<string>();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = itemUrns
                .Select(urn => new BatchGetVersionResultDto
                {
                    Urn = urn,
                    ItemUrn = urn,
                    Name = "File.rvt",
                    CustomAttributes = []
                })
                .ToArray(),
            Errors = []
        };

        _handlerMock.OnRequest = request =>
        {
            requestUrls.Add(request.RequestUri?.ToString() ?? string.Empty);
            return CreateJsonResponse(mockResponse);
        };

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, itemUrns);

        // Assert
        // This is the regression guard for the change that took a single file creation from ~30s to
        // a couple of seconds: reading attribute values is ONE batch request, regardless of how many
        // items were asked for. It previously also issued a per-item path request for every urn -
        // and the caller discarded both fields those requests populated.
        result.Should().HaveCount(3);
        requestUrls.Should().HaveCount(1);
        requestUrls[0].Should().Contain("versions:batch-get");
        requestUrls.Should().NotContain(url => url.Contains("/items/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetItemCustomAttributesAsync_WithResultMissingItemUrn_DropsIt()
    {
        // Arrange
        var projectId = "b.projectId123";
        var withItemUrn = "urn:adsk.bim360:dm.item:itemId1:dm.version:versionId1";
        var withoutItemUrn = "urn:adsk.bim360:dm.item:itemId2:dm.version:versionId2";
        var service = CreateService();

        var mockResponse = new BatchGetVersionsResponseDto
        {
            Results = new[]
            {
                new BatchGetVersionResultDto
                {
                    Urn = withItemUrn,
                    ItemUrn = withItemUrn,
                    Name = "Good.rvt",
                    CustomAttributes = new[] { new CustomAttributeDto { Name = "Status", Value = "S0" } }
                },
                new BatchGetVersionResultDto
                {
                    Urn = withoutItemUrn,
                    // No itemUrn: nothing can match this row back to the file it describes.
                    Name = "Unmatchable.rvt",
                    CustomAttributes = new[] { new CustomAttributeDto { Name = "Status", Value = "S2" } }
                }
            },
            Errors = []
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.GetItemCustomAttributesAsync(projectId, new[] { withItemUrn, withoutItemUrn });

        // Assert
        // Dropped rather than carried through with an empty urn. Keying it on "" is what used to
        // happen, and because callers build a urn -> result dictionary from these, two such rows
        // silently overwrote each other - so one file's attributes could be shown against another's.
        var resultList = result.ToList();
        resultList.Should().HaveCount(1);
        resultList[0].Urn.Should().Be(withItemUrn);
        resultList.Should().NotContain(r => string.IsNullOrEmpty(r.Urn));
    }
    #endregion

    #region UpdateItemCustomAttributesAsync Tests

    [Fact]
    public async Task UpdateItemCustomAttributesAsync_WithValidUpdates_ReturnsMappedResults()
    {
        // Arrange
        var projectId = "b.projectId123";
        var versionUrn = "urn:adsk.wipprod:fs.file:vf.abc?version=1";
        var service = CreateService();
        var updates = new[] { new CustomAttributeUpdateRequestDto(102, "v3") };

        var mockResponse = new CustomAttributesBatchUpdateResponseDto
        {
            Results = new[]
            {
                new CustomAttributesBatchUpdateResultItemDto { Id = 102, Name = "n2", Type = "array", Value = "v3" }
            }
        };

        _handlerMock.SetupResponse(HttpStatusCode.OK, mockResponse);

        // Act
        var result = await service.UpdateItemCustomAttributesAsync(projectId, versionUrn, updates);

        // Assert
        result.Should().ContainSingle();
        result[0].AttributeId.Should().Be(102);
        result[0].Name.Should().Be("n2");
        result[0].Value.Should().Be("v3");
        _handlerMock.LastRequestUrl.Should().Contain($"/versions/{Uri.EscapeDataString(versionUrn)}/custom-attributes:batch-update");
        _handlerMock.LastRequestUrl.Should().Contain("projectId123");
        _handlerMock.LastRequestUrl.Should().NotContain("b.projectId123");
    }

    [Fact]
    public async Task UpdateItemCustomAttributesAsync_WithNoUpdates_ReturnsEmptyWithoutCallingApi()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.UpdateItemCustomAttributesAsync(
            "b.projectId123", "urn:version", Array.Empty<CustomAttributeUpdateRequestDto>());

        // Assert
        result.Should().BeEmpty();
        _handlerMock.LastRequestUrl.Should().BeNull();
    }

    [Fact]
    public async Task UpdateItemCustomAttributesAsync_WithForbidden_ThrowsPermissionDeniedException()
    {
        // Arrange
        var service = CreateService();
        var updates = new[] { new CustomAttributeUpdateRequestDto(102, "v3") };

        _handlerMock.SetupResponse(HttpStatusCode.Forbidden, (object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<global::DigitalMIDP.Application.Exceptions.PermissionDeniedException>(
            () => service.UpdateItemCustomAttributesAsync("b.projectId123", "urn:version", updates));
    }

    [Fact]
    public async Task UpdateItemCustomAttributesAsync_WithBadRequest_ThrowsAttributeValidationException()
    {
        // Arrange
        var service = CreateService();
        var updates = new[] { new CustomAttributeUpdateRequestDto(102, "not-a-valid-option") };

        _handlerMock.SetupResponse(HttpStatusCode.BadRequest, (object?)null);

        // Act & Assert
        await Assert.ThrowsAsync<global::DigitalMIDP.Application.Exceptions.AttributeValidationException>(
            () => service.UpdateItemCustomAttributesAsync("b.projectId123", "urn:version", updates));
    }

    [Fact]
    public async Task UpdateItemCustomAttributesAsync_WithNetworkFailure_ThrowsApplicationException()
    {
        // Arrange
        var service = CreateService();
        var updates = new[] { new CustomAttributeUpdateRequestDto(102, "v3") };

        _handlerMock.OnRequest = _ => throw new HttpRequestException("Network unreachable");

        // Act & Assert
        await Assert.ThrowsAsync<global::DigitalMIDP.Application.Exceptions.ApplicationException>(
            () => service.UpdateItemCustomAttributesAsync("b.projectId123", "urn:version", updates));
    }

    #endregion

    private static HttpResponseMessage CreateJsonResponse<T>(T content)
    {
        var jsonContent = JsonSerializer.Serialize(content);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _handlerMock?.Dispose();
    }

    #region Caching

    [Fact]
    public async Task GetRootFolderAsync_WithRepeatedCalls_IssuesOneRequest()
    {
        var service = CreateService();
        var requests = 0;

        _handlerMock.OnRequest = request =>
        {
            requests++;
            return CreateJsonResponse(new
            {
                data = new[]
                {
                    new { id = "root", type = "folders", attributes = new { name = "Project Files", displayName = "Project Files" } }
                }
            });
        };

        for (var i = 0; i < 4; i++)
        {
            var folder = await service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);
            folder.Id.Should().Be("root");
        }

        // A project's "Project Files" folder is fixed for the life of the project, yet this was
        // re-resolved at the start of every file listing, every CDE-structure resolution and every
        // deliverable creation.
        requests.Should().Be(1);
    }

    [Fact]
    public async Task GetRootFolderAsync_AfterInvalidateProjectCache_AsksApsAgain()
    {
        var service = CreateService();
        var requests = 0;

        _handlerMock.OnRequest = request =>
        {
            requests++;
            return CreateJsonResponse(new
            {
                data = new[]
                {
                    new { id = "root", type = "folders", attributes = new { name = "Project Files", displayName = "Project Files" } }
                }
            });
        };

        await service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);
        service.InvalidateProjectCache("b.p1");
        await service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);

        // This is what makes an explicit Refresh mean "go and ask APS again" rather than "re-read
        // what you already had" - without it, a change made in ACC's web UI would not appear until
        // the entry expired and Refresh would look broken.
        requests.Should().Be(2);
    }

    [Fact]
    public async Task InvalidateProjectCache_DoesNotAffectADifferentProject()
    {
        var service = CreateService();
        var requestedProjects = new List<string>();

        _handlerMock.OnRequest = request =>
        {
            requestedProjects.Add(request.RequestUri!.ToString());
            return CreateJsonResponse(new
            {
                data = new[]
                {
                    new { id = "root", type = "folders", attributes = new { name = "Project Files", displayName = "Project Files" } }
                }
            });
        };

        await service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);
        await service.GetRootFolderAsync("hub1", "b.p2", "token", CancellationToken.None);

        service.InvalidateProjectCache("b.p1");

        await service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);
        await service.GetRootFolderAsync("hub1", "b.p2", "token", CancellationToken.None);

        requestedProjects.Count(u => u.Contains("b.p1")).Should().Be(2);
        requestedProjects.Count(u => u.Contains("b.p2")).Should().Be(1);
    }

    [Fact]
    public async Task GetItemPathMetadataAsync_WithRepeatedCalls_IssuesOneRequestPerItem()
    {
        var service = CreateService();
        var requests = 0;

        _handlerMock.OnRequest = request =>
        {
            requests++;
            return CreateJsonResponse(new
            {
                data = new
                {
                    attributes = new { pathInProject = "/Project Files/WIP/" },
                    links = new { webView = new { href = "https://acc.test/item" } }
                }
            });
        };

        for (var i = 0; i < 5; i++)
        {
            var metadata = await service.GetItemPathMetadataAsync("b.p1", "urn:item1", CancellationToken.None);
            metadata!.WebViewLink.Should().Be("https://acc.test/item");
        }

        // The per-item path endpoint is the most expensive thing the application reads: a 28-file
        // project cost 28 requests, re-issued on every refresh and every deliverable creation, for a
        // path that only changes if somebody moves the file in ACC.
        requests.Should().Be(1);
    }

    [Fact]
    public async Task GetItemPathMetadataAsync_WhenTheResponseCarriesNoItemData_ReturnsNullWithoutCachingIt()
    {
        var service = CreateService();
        var requests = 0;

        _handlerMock.OnRequest = request =>
        {
            requests++;
            return CreateJsonResponse(new { data = (object?)null });
        };

        (await service.GetItemPathMetadataAsync("b.p1", "urn:item1", CancellationToken.None)).Should().BeNull();
        (await service.GetItemPathMetadataAsync("b.p1", "urn:item1", CancellationToken.None)).Should().BeNull();

        // Returning null keeps the caller existing item untouched, so a file whose path could not be
        // read keeps whatever path it already had instead of having it blanked out. Not cached
        // either - a null would otherwise pin "no path" onto the file for the whole cache lifetime.
        requests.Should().Be(2);
    }

    #endregion

    #region Response validation

    [Fact]
    public async Task GetFolderItemsAsync_DropsEntriesWithNoIdOrName()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => CreateJsonResponse(new
        {
            data = new object[]
            {
                new { id = "f1", type = "items", attributes = new { displayName = "Good.rvt" } },
                new { id = "", type = "items", attributes = new { displayName = "NoId.rvt" } },
                new { id = "f3", type = "items", attributes = new { displayName = (string?)null } }
            }
        });

        var items = (await service.GetFolderItemsAsync("b.p1", "folder1", "token", CancellationToken.None)).ToList();

        // An entry with neither an id nor a name becomes a grid row the user can see but cannot
        // open, edit or match to a file in ACC - and a folder with a blank id would be recursed into
        // as "", returning some other folder contents.
        items.Should().ContainSingle().Which.Name.Should().Be("Good.rvt");
    }

    [Fact]
    public async Task GetFolderItemsAsync_WithANullDataArray_ReturnsEmptyRatherThanThrowing()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => CreateJsonResponse(new { data = (object?)null, links = (object?)null });

        var items = await service.GetFolderItemsAsync("b.p1", "folder1", "token", CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRootFolderAsync_WithNoProjectFilesFolder_ThrowsResourceNotFound()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => CreateJsonResponse(new
        {
            data = new[]
            {
                new { id = "other", type = "folders", attributes = new { name = "Plans", displayName = "Plans" } }
            }
        });

        var act = () => service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);

        await act.Should().ThrowAsync<global::DigitalMIDP.Application.Exceptions.ResourceNotFoundException>();
    }

    [Fact]
    public async Task GetRootFolderAsync_WithAFolderThatHasNoId_IgnoresIt()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => CreateJsonResponse(new
        {
            data = new[]
            {
                new { id = "", type = "folders", attributes = new { name = "Project Files", displayName = "Project Files" } }
            }
        });

        var act = () => service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);

        // Every subsequent folder listing is keyed on this id, so a blank one is not a usable root.
        await act.Should().ThrowAsync<global::DigitalMIDP.Application.Exceptions.ResourceNotFoundException>();
    }

    [Fact]
    public async Task GetRootFolderAsync_WithAnEmptyBody_FailsWithAReadableMessage()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };

        var act = () => service.GetRootFolderAsync("hub1", "b.p1", "token", CancellationToken.None);

        // The type is the point. Previously this surfaced as a bare framework exception
        // (NullReferenceException, or an untyped InvalidOperationException from the JSON helper) that
        // slipped past every catch in the service layer, so callers could not tell it apart from a
        // permission or validation failure.
        await act.Should().ThrowAsync<global::DigitalMIDP.Application.Exceptions.ApplicationException>()
            .WithMessage("*could not be read*");
    }

    [Fact]
    public async Task GetHubsAsync_DropsHubsWithNoId()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => CreateJsonResponse(new
        {
            data = new[]
            {
                new { id = "a.hub1", attributes = new { name = "Hub One", region = "US" } },
                new { id = "", attributes = new { name = "Broken Hub", region = "US" } }
            },
            links = (object?)null
        });

        var hubs = (await service.GetHubsAsync("token", CancellationToken.None)).ToList();

        // A hub with no id cannot be used to list projects, so offering it in the picker would give
        // the user an entry that fails the moment it is chosen.
        hubs.Should().ContainSingle().Which.Name.Should().Be("Hub One");
    }

    [Fact]
    public async Task GetHubsAsync_WithANullBody_FailsWithAReadableMessage()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };

        var act = () => service.GetHubsAsync("token", CancellationToken.None);

        await act.Should().ThrowAsync<global::DigitalMIDP.Application.Exceptions.ApplicationException>();
    }

    [Fact]
    public async Task GetProjectsAsync_DropsProjectsWithNoId()
    {
        var service = CreateService();

        _handlerMock.OnRequest = _ => CreateJsonResponse(new
        {
            data = new[]
            {
                new { id = "b.p1", attributes = new { name = "Project One" } },
                new { id = "", attributes = new { name = "Broken Project" } }
            },
            links = (object?)null
        });

        var projects = (await service.GetProjectsAsync("a.hub1", "token", CancellationToken.None)).ToList();

        projects.Should().ContainSingle().Which.Name.Should().Be("Project One");
    }

    #endregion
}
/// <summary>
/// Mock HttpMessageHandler for testing HttpClient interactions.
/// </summary>
internal sealed class HttpMessageHandlerMock : HttpMessageHandler
{
    // Stores what to build each response from, rather than a single HttpResponseMessage instance -
    // several production code paths (e.g. GetItemPathMetadataAsync) issue concurrent requests, and
    // handing out the same instance's Content stream to more than one in-flight reader throws
    // ObjectDisposedException once the first reader consumes it.
    private (HttpStatusCode StatusCode, string? Json)? _response;
    public string? LastRequestUrl { get; private set; }
    public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }

    public void SetupResponse<T>(HttpStatusCode statusCode, T? content)
    {
        _response = (content == null && statusCode != HttpStatusCode.OK)
            ? (statusCode, null)
            : (statusCode, System.Text.Json.JsonSerializer.Serialize(content));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUrl = request.RequestUri?.ToString();

        if (OnRequest != null)
        {
            return await Task.FromResult(OnRequest(request));
        }

        if (_response is not { } response)
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }

        return response.Json is null
            ? new HttpResponseMessage(response.StatusCode)
            : new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Json, System.Text.Encoding.UTF8, "application/json")
            };
    }
}
