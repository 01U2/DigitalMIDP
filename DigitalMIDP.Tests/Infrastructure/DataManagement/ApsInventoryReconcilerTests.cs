using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalMIDP.Application.Manifests;
using DigitalMIDP.Infrastructure.DataManagement;
using Xunit;

namespace DigitalMIDP.Tests.Infrastructure.DataManagement;

public sealed class ApsInventoryReconcilerTests
{
    private const string Lineage = "urn:adsk.wipprod:dm.lineage:drawing";
    private const string Tip = "urn:adsk.wipprod:fs.file:vf.drawing?version=3";
    private const string Web = "https://acc.autodesk.com/docs/files/projects/project?entityId=drawing";
    [Fact]
    public async Task CompleteScansDetectMetadataMovesRenamesAndRemoval()
    {
        var handler = new InventoryHandler();
        using var client = new HttpClient(handler);
        var reader = new ApsInventoryReconciler(client);
        Task<InventoryReconciliation> Scan(ProjectManifest? previous) => reader.ReconcileAsync("hub", "b.project", previous);
        var initial = await Scan(null);
        Assert.True(initial.Changed);
        Assert.Equal(1L, initial.Manifest.Revision);
        Assert.Equal(2, initial.Manifest.Folders.Count);
        Assert.Null(initial.Manifest.Folders.Single(f => f.FolderId == "child").Files.Single().Values[9]);
        var child = initial.Manifest.Folders.Single(f => f.FolderId == "child");
        Assert.Equal("Status", child.Definitions.Single(d => d.AttributeId == 9).Name);
        Assert.Equal("Ground plan", child.Files.Single().Values[11]);
        Assert.Equal("Ground plan", child.Files.Single().Item.CustomAttributes!["Description"]);
        Assert.Equal(Web, child.Files.Single().Item.WebViewLink);
        var unchanged = await Scan(initial.Manifest);
        Assert.False(unchanged.Changed);
        Assert.Same(initial.Manifest, unchanged.Manifest);
        handler.Value = "Ground floor";
        var edited = await Scan(unchanged.Manifest);
        Assert.True(edited.Changed);
        Assert.Equal("Ground floor", edited.Manifest.Folders.Single(f => f.FolderId == "child").Files.Single().Item.CustomAttributes!["Status"]);
        handler.ChildName = "01 - WIP";
        var renamed = await Scan(edited.Manifest);
        Assert.Equal("Project Files/01 - WIP", renamed.Manifest.Folders.Single(f => f.FolderId == "child").Files.Single().Item.PathInProject);
        handler.FileFolder = "root";
        var moved = await Scan(renamed.Manifest);
        var file = moved.Manifest.Folders.Single(f => f.FolderId == "root").Files.Single();
        Assert.Equal("root", file.Item.ParentId);
        Assert.Equal("Ground floor", file.Values[7]);
        handler.FileFolder = null;
        var removed = await Scan(moved.Manifest);
        Assert.Equal(5L, removed.Manifest.Revision);
        Assert.Empty(removed.Manifest.Folders.SelectMany(f => f.Files));
    }

    [Theory]
    [InlineData("batch", "{\"results\":[]}")]
    [InlineData("batch", "{\"results\":null}")]
    [InlineData("batch", "{\"errors\":[{}],\"results\":[]}")]
    [InlineData("batch", "{\"results\":[{\"itemUrn\":\"" + Lineage + "\",\"urn\":\"" + Tip + "\",\"customAttributes\":null}]}")]
    [InlineData("batch", "{\"results\":[{\"itemUrn\":\"" + Lineage + "\",\"urn\":\"stale\",\"customAttributes\":[]}]}")]
    [InlineData("batch", "{\"results\":[{\"itemUrn\":\"" + Lineage + "\",\"urn\":\"" + Tip + "\",\"customAttributes\":[{\"id\":9,\"name\":\"Description\",\"value\":null}]}]}")]
    [InlineData("batch", "{\"results\":[{\"itemUrn\":\"" + Lineage + "\",\"urn\":\"" + Tip + "\",\"customAttributes\":[{\"name\":\"Unknown\",\"value\":null}]}]}")]
    [InlineData("project-schema", "{\"metadata\":[]}")]
    [InlineData("detail", "{\"data\":{\"id\":\"other\"}}")]
    [InlineData("schema", "{}")]
    [InlineData("page2", "{\"data\":null}")]
    [InlineData("page2", "{\"data\":[],\"links\":{\"next\":{\"href\":\"?second\"}}}")]
    [InlineData("page2", "{\"data\":[],\"links\":{\"next\":{\"href\":\"https://example.org/other\"}}}")]
    public async Task IncompleteScanDoesNotReplacePrevious(string route, string response)
    {
        var handler = new InventoryHandler();
        using var client = new HttpClient(handler);
        var reader = new ApsInventoryReconciler(client);
        var previous = (await reader.ReconcileAsync("hub", "b.project", null)).Manifest;
        handler.Override = (route, response);
        if (route == "detail") handler.FirstVersion++;
        var error = await Record.ExceptionAsync(() => reader.ReconcileAsync("hub", "b.project", previous));
        Assert.True(error is InvalidDataException or InvalidOperationException or KeyNotFoundException or JsonException, error?.ToString());
        Assert.Equal(1L, previous.Revision);
    }

    [Fact]
    public async Task BatchesAtMost500AndMatchesReorderedResultsByIdentity()
    {
        var handler = new InventoryHandler { FileCount = 501 };
        using var client = new HttpClient(handler);
        var result = await new ApsInventoryReconciler(client).ReconcileAsync("hub", "b.project", null);
        Assert.Equal(new[] { 500, 1 }, handler.BatchSizes);
        Assert.Equal(501, result.Manifest.Folders.SelectMany(f => f.Files).Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetailGetsAreLimitedToNewOrChangedFilesWithoutListingLinks(bool hasDetailLink)
    {
        var handler = new InventoryHandler { FileCount = 3, HasDetailLink = hasDetailLink };
        using var client = new HttpClient(handler);
        var reader = new ApsInventoryReconciler(client);
        Task<InventoryReconciliation> Scan(ProjectManifest? previous) => reader.ReconcileAsync("hub", "b.project", previous);
        static string? Link(InventoryReconciliation scan, string id) => scan.Manifest.Folders.SelectMany(f => f.Files)
            .Single(f => f.Item.Id == id).Item.WebViewLink;

        var cold = await Scan(null);
        Assert.Equal(new[] { Lineage, Lineage + "1", Lineage + "2" }, handler.DetailGets);
        Assert.Equal(hasDetailLink ? Web : null, Link(cold, Lineage));
        handler.DetailGets.Clear();
        var warm = await Scan(cold.Manifest);
        Assert.Empty(handler.DetailGets);
        Assert.Same(cold.Manifest, warm.Manifest);

        handler.FirstVersion++;
        handler.HasDetailLink = !hasDetailLink;
        var changed = await Scan(warm.Manifest);
        Assert.Equal(new[] { Lineage }, handler.DetailGets);
        Assert.Equal(hasDetailLink ? null : Web, Link(changed, Lineage));
        Assert.Equal(hasDetailLink ? Web : null, Link(changed, Lineage + "1"));

        handler.DetailGets.Clear();
        handler.FirstListingLink = Web + "&source=listing";
        var listed = await Scan(changed.Manifest);
        Assert.Empty(handler.DetailGets);
        Assert.Equal(handler.FirstListingLink, Link(listed, Lineage));
    }

    private sealed class InventoryHandler : HttpMessageHandler
    {
        public string? Value;
        public string ChildName = "Design";
        public string? FileFolder = "child";
        public int FileCount = 1;
        public int FirstVersion = 3;
        public string? FirstListingLink;
        public bool HasDetailLink = true;
        public List<string> DetailGets { get; } = [];
        public List<int> BatchSizes { get; } = [];
        public (string Route, string Json)? Override;
        private static object Folder(string id, string displayName) => new { id, type = "folders", attributes = new { displayName } };
        private string Version(string id) => id == Lineage ? Tip.Replace("version=3", $"version={FirstVersion}") : Tip.Replace("drawing", id.Split(':')[^1]);
        private object FileRow(string id) => new { id, type = "items", attributes = new { displayName = "Drawing.pdf" }, relationships = new { tip = new { data = new { id = Version(id) } } },
            links = id == Lineage && FirstListingLink != null ? (object)new { webView = new { href = FirstListingLink } } : new { } };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Assert.Equal("developer.api.autodesk.com", uri.Host);
            var path = uri.AbsolutePath;
            object payload;
            string route = "";
            if (path == "/project/v1/hubs/hub/projects/b.project/topFolders") payload = new { data = new[] { Folder("root", "Project Files") } };
            else if (path == "/data/v1/projects/b.project/folders/root/contents" && uri.Query != "?second")
                payload = new { data = new[] { Folder("admin", "00 ProjectFiles") }, links = new { next = new { href = "?second" } } };
            else if (path is "/data/v1/projects/b.project/folders/root/contents" or "/data/v1/projects/b.project/folders/child/contents")
            {
                var root = path.Contains("/root/");
                route = root ? "page2" : "child";
                var data = new List<object>();
                if (root) data.Add(Folder("child", ChildName));
                if (FileFolder == (root ? "root" : "child"))
                    data.AddRange(Enumerable.Range(0, FileCount).Select(i => FileRow(i == 0 ? Lineage : Lineage + i)));
                payload = new { data };
            }
            else if (path == "/bim360/docs/v1/projects/project/custom-attribute-definitions")
            {
                route = "project-schema";
                payload = new { definitions = new[] { new { id = 11, name = "Description", type = "STRING" }, new { id = 9, name = "Project status", type = "STRING" } } };
            }
            else if (path.StartsWith("/data/v1/projects/b.project/items/", StringComparison.Ordinal))
            {
                route = "detail";
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("?includePathInProject=true", uri.Query);
                var id = Uri.UnescapeDataString(path.Split('/')[^1]);
                Assert.StartsWith(Lineage, id);
                DetailGets.Add(id);
                payload = new { data = new { type = "items", id, attributes = new { pathInProject = new[] { "Project Files", ChildName, "Drawing.pdf" } }, links = HasDetailLink ? (object)new { webView = new { href = Web } } : new { } } };
            }
            else if (path is "/bim360/docs/v1/projects/project/folders/root/custom-attribute-definitions" or "/bim360/docs/v1/projects/project/folders/child/custom-attribute-definitions")
            {
                route = "schema";
                payload = new { results = new[] { new { id = path.Contains("/root/") ? 7 : 9, name = "Status", type = "STRING" } } };
            }
            else if (path == "/bim360/docs/v1/projects/project/versions:batch-get")
            {
                route = "batch";
                Assert.Equal(HttpMethod.Post, request.Method);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = body.RootElement.GetProperty("urns").EnumerateArray().Select(e => e.GetString()!).ToArray();
                BatchSizes.Add(ids.Length);
                Assert.All(ids, id => Assert.StartsWith(Lineage, id));
                payload = new { results = ids.Reverse().Select(id => new { itemUrn = id, urn = Version(id),
                    customAttributes = new object[] { new { id = FileFolder == "root" ? 7 : 9, name = "Status", value = Value }, new { name = "description", value = "Ground plan" } } }) };
            }
            else throw new Xunit.Sdk.XunitException("Unexpected inventory request: " + uri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = Override is { } replacement && replacement.Route == route
                ? new StringContent(replacement.Json) : JsonContent.Create(payload) };
        }
    }
}
