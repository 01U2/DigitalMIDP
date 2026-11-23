using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalMIDP.Application.Interfaces;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using NSubstitute;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.DataManagement;

public sealed class ApsManifestPublisherTests
{
    private static ServiceProvider Provider(Remote remote)
    {
        var services = new ServiceCollection();
        services.AddApsServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Autodesk:ClientId"] = "mock", ["Autodesk:RedirectUri"] = "http://localhost/callback"
        }).Build());
        var token = Substitute.For<ITokenProvider>();
        token.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test-token");
        services.AddSingleton(token);
        services.PostConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = remote));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ChecksCreateReadThenVersionTheSameItem()
    {
        var remote = new Remote();
        using var provider = Provider(remote);
        var publisher = provider.GetRequiredService<IManifestPublisher>();
        async Task<ManifestCheckResult> Check()
        {
            var before = remote.Sends;
            var result = await publisher.CheckAsync("h", "b.p");
            Assert.Equal(remote.Sends - before, result.HttpRequestCount);
            Assert.True(result.CheckedAtUtc >= result.LastPublicationUtc);
            return result;
        }
        var first = await Check();
        Assert.True(first.Changed);
        Assert.Equal(2, remote.FoldersCreated);
        var same = await Check();
        Assert.False(same.Changed);
        Assert.Equal(first.LastPublicationUtc, same.LastPublicationUtc);
        remote.ExtraFolder = true;
        Assert.True((await Check()).Changed);
        Assert.Equal(1, remote.ItemsCreated);
        Assert.Equal(1, remote.VersionsCreated);
        var manifest = ProjectManifestSerializer.Deserialize(remote.Content!, "h", "b.p", 2);
        Assert.Equal(2, manifest.Folders.Count);
        Assert.Equal("drawing.pdf", Assert.Single(manifest.Folders.SelectMany(f => f.Files)).Item.Name);
    }

    [Theory]
    [InlineData("wrong-root")]
    [InlineData("duplicate-template")]
    [InlineData("wrong-project")]
    [InlineData("corrupt")]
    [InlineData("schema-failure")]
    [InlineData("upload-failure")]
    public async Task FailedChecksDoNotCommit(string failure)
    {
        var remote = new Remote { FoldersCreated = 2, Failure = failure,
            Content = failure == "wrong-project" ? ProjectManifestSerializer.Serialize(new(1, "h", "other", 1, DateTimeOffset.UtcNow, [])) : failure == "corrupt" ? "{}" : null };
        using var provider = Provider(remote);
        var error = await Record.ExceptionAsync(() => provider.GetRequiredService<IManifestPublisher>().CheckAsync("h", "b.p"));
        Assert.NotNull(error);
        Assert.False(error is Xunit.Sdk.XunitException, error.ToString());
        Assert.Equal(2, remote.FoldersCreated);
        Assert.Equal(0, remote.ItemsCreated + remote.VersionsCreated);
    }

    private sealed class Remote : HttpMessageHandler
    {
        public int Sends, FoldersCreated, ItemsCreated, VersionsCreated;
        public bool ExtraFolder;
        public string? Content, Failure;
        private string? staged;
        private const string Storage = "urn:adsk.objects:os.object:bucket/key";
        private static object Folder(string id, string displayName) => new { type = "folders", id, attributes = new { displayName } };
        private static object File(string id, string? name = null) => new { type = "items", id, attributes = new { displayName = name ?? ProjectManifest.FileName.ToUpperInvariant() }, relationships = new { tip = new { data = new { id = "urn:tip" } } } };
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        private static HttpResponseMessage Rows(params object[] rows) => Json(new { data = rows });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sends++;
            var uri = request.RequestUri!;
            if (uri.Host == "signed.invalid")
            {
                Assert.Null(request.Headers.Authorization);
                if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent(Content!) };
                Assert.Equal(HttpMethod.Put, request.Method);
                staged = await request.Content!.ReadAsStringAsync(ct);
                return new(Failure == "upload-failure" ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
            }
            Assert.Equal("developer.api.autodesk.com", uri.Host);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            const string project = "/data/v1/projects/b.p/";
            switch (uri.AbsolutePath)
            {
                case "/project/v1/hubs/h/projects/b.p/topFolders": return Rows(Folder("root", Failure == "wrong-root" ? "Other" : "Project Files"));
                case project + "folders/root/contents":
                    var rows = new List<object> { File("excluded") };
                    if (FoldersCreated > 0) rows.Add(Folder("admin", "00 - Project Files"));
                    if (ExtraFolder) rows.Add(Folder("child", "WIP"));
                    return Rows(rows.ToArray());
                case project + "folders/admin/contents":
                    if (Failure == "duplicate-template") return Rows(Folder("a", "Templates"), Folder("b", "Template"));
                    return FoldersCreated == 2 ? Rows(Folder("templates", "Project-Template")) : Rows();
                case project + "folders/child/contents": return Rows(File("drawing", "drawing.pdf"));
                case "/bim360/docs/v1/projects/p/versions:batch-get":
                    return Json(new { results = new[] { new { itemUrn = "drawing", urn = "urn:tip", customAttributes = Array.Empty<object>() } } });
                case project + "folders/templates/contents": return Content == null ? Rows() : Rows(File("existing"));
                case project + "folders":
                    using (var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                        Assert.Equal(FoldersCreated == 0 ? "root" : "admin", doc.RootElement.GetProperty("data").GetProperty("relationships").GetProperty("parent").GetProperty("data").GetProperty("id").GetString());
                    return Json(new { data = new { id = ++FoldersCreated == 1 ? "admin" : "templates" } });
                case project + "items/drawing": return Json(new { data = new { id = "drawing", type = "items" } });
                case "/bim360/docs/v1/projects/p/custom-attribute-definitions":
                case "/bim360/docs/v1/projects/p/folders/root/custom-attribute-definitions":
                case "/bim360/docs/v1/projects/p/folders/child/custom-attribute-definitions":
                    return Failure == "schema-failure" ? new(HttpStatusCode.Forbidden) : Json(Array.Empty<object>());
                case project + "items/existing/tip": return Json(new { data = new { id = "urn:tip", relationships = new { storage = new { data = new { id = Storage } } } } });
                case project + "storage": return Json(new { data = new { id = Storage } });
                case "/oss/v2/buckets/bucket/objects/key/signeds3download": return Json(new { url = "https://signed.invalid/key", status = "complete" });
                case "/oss/v2/buckets/bucket/objects/key/signeds3upload": return Json(new { urls = new[] { "https://signed.invalid/key" }, uploadKey = "key" });
                case project + "items":
                    Assert.Null(Content);
                    ItemsCreated++;
                    Content = staged;
                    return Json(new { data = new { id = "existing" }, included = new[] { new { type = "versions", id = "urn:tip" } } });
                case project + "versions":
                    using (var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
                    {
                        var relationships = doc.RootElement.GetProperty("data").GetProperty("relationships");
                        Assert.Equal("existing", relationships.GetProperty("item").GetProperty("data").GetProperty("id").GetString());
                        Assert.Equal(Storage, relationships.GetProperty("storage").GetProperty("data").GetProperty("id").GetString());
                    }
                    VersionsCreated++;
                    Content = staged;
                    return Json(new { data = new { type = "versions", id = "urn:next" } });
                default: throw new Xunit.Sdk.XunitException("Unexpected mock request: " + uri.AbsolutePath);
            }
        }
    }
}