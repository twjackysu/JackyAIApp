using System.Net;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>Finding a server in the official MCP Registry (D-122), against a registry played by a handler.</summary>
    public class McpRegistryTests
    {
        private readonly List<HttpRequestMessage> _asked = [];
        private Func<HttpRequestMessage, HttpResponseMessage> _registry = _ => StubHttpMessageHandler.JsonResponse("""{ "servers": [] }""");

        private McpRegistry Registry(IMemoryCache? cache = null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(McpRegistry.HttpClientName)).Returns(() => new HttpClient(new StubHttpMessageHandler(request =>
            {
                _asked.Add(request);
                return _registry(request);
            })));
            return new McpRegistry(factory.Object, cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<McpRegistry>.Instance);
        }

        private static string Page(params string[] entries) => PageThen(null, entries);

        /// <summary>A page whose next is <paramref name="nextCursor"/> (null: the last).</summary>
        private static string PageThen(string? nextCursor, params string[] entries) =>
            $$"""{ "servers": [{{string.Join(",", entries)}}], "metadata": { "count": {{entries.Length}}{{(nextCursor == null ? "" : $", \"nextCursor\": \"{nextCursor}\"")}} } }""";

        private static string Server(string name, string remotes, string extra = "", string status = "active") => $$"""
            { "server": { "name": "{{name}}", "description": "Does things.", "version": "1.0.0", "remotes": {{remotes}} {{extra}} },
              "_meta": { "io.modelcontextprotocol.registry/official": { "status": "{{status}}", "isLatest": true } } }
            """;

        private static string Remote(string url, string type = "streamable-http", string headers = "") =>
            $$"""[{ "type": "{{type}}", "url": "{{url}}" {{headers}} }]""";

        [Fact]
        public async Task ReadsTheLatestVersionOfEveryServer_PageByPage_AndNoSearchReachesTheRegistry()
        {
            _registry = request => StubHttpMessageHandler.JsonResponse(request.RequestUri!.Query.Contains("cursor=p%2F2")
                ? PageThen(null, Server("io.example/two", Remote("https://two.example.com/mcp")))
                : PageThen("p/2", Server("io.example/one", Remote("https://one.example.com/mcp"))));

            var result = await Registry().SearchAsync("  two&x  ", default);

            Assert.Equal(2, _asked.Count);
            Assert.All(_asked, r =>
            {
                Assert.StartsWith(McpRegistry.BaseUrl + "?", r.RequestUri!.AbsoluteUri);
                Assert.Contains("version=latest", r.RequestUri.AbsoluteUri);
                Assert.Contains($"limit={McpRegistry.PageSize}", r.RequestUri.AbsoluteUri);
                Assert.DoesNotContain("search=", r.RequestUri.AbsoluteUri);
            });
            Assert.Empty(result.Data!.Servers);
        }

        [Fact]
        public async Task ARepeatedCursor_EndsTheWalk()
        {
            _registry = _ => StubHttpMessageHandler.JsonResponse(PageThen("same", Server("io.example/one", Remote("https://one.example.com/mcp"))));

            var result = await Registry().SearchAsync("", default);

            Assert.Equal(2, _asked.Count);
            Assert.Single(result.Data!.Servers);
        }

        [Fact]
        public async Task APageThatFailsLater_KeepsWhatWasRead()
        {
            _registry = request => request.RequestUri!.Query.Contains("cursor=")
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : StubHttpMessageHandler.JsonResponse(PageThen("next", Server("io.example/one", Remote("https://one.example.com/mcp"))));

            var result = await Registry().SearchAsync("", default);

            Assert.Equal("io.example/one", Assert.Single(result.Data!.Servers).Name);
        }

        [Fact]
        public async Task ASearchMatchesNameTitleOrDescription_EveryWord_NamesFirst()
        {
            _registry = _ => StubHttpMessageHandler.JsonResponse(PageThen(null,
                Server("io.example/notes", Remote("https://notes.example.com/mcp"), """, "description": "Weather notes for travel." """),
                Server("io.example/forecast", Remote("https://forecast.example.com/mcp"), """, "title": "Weather Forecast" """),
                Server("io.example/other", Remote("https://other.example.com/mcp"))));

            var found = (await Registry().SearchAsync("WEATHER", default)).Data!.Servers.Select(s => s.Name).ToList();
            var both = (await Registry().SearchAsync("weather travel", default)).Data!.Servers.Select(s => s.Name).ToList();

            Assert.Equal(new[] { "io.example/forecast", "io.example/notes" }, found);
            Assert.Equal(new[] { "io.example/notes" }, both);
        }

        [Fact]
        public void TheSearchIsOneLineAndCut() =>
            Assert.Equal(new string('a', McpRegistry.MaxSearchLength), McpRegistry.CleanSearch("\n" + new string('a', 80) + "\t"));

        [Fact]
        public async Task KeepsOnlyServersReachableOverStreamableHttpAtAFixedHttpsAddress()
        {
            _registry = _ => StubHttpMessageHandler.JsonResponse(Page(
                Server("io.example/good", Remote("https://MCP.Example.com/mcp/")),
                Server("io.example/sse-only", Remote("https://sse.example.com/sse", "sse")),
                Server("io.example/template", Remote("https://{tenant}.example.com/mcp")),
                Server("io.example/plain-http", Remote("http://insecure.example.com/mcp")),
                Server("io.example/local-only", "[]"),
                Server("io.example/deprecated", Remote("https://old.example.com/mcp"), status: "deprecated"),
                Server("not a name", Remote("https://bad-name.example.com/mcp"))));

            var result = await Registry().SearchAsync("", default);

            var server = Assert.Single(result.Data!.Servers);
            Assert.Equal("io.example/good", server.Name);
            Assert.Equal("https://mcp.example.com/mcp", server.Url);
            Assert.Equal("good", server.Title);
            Assert.Equal("Does things.", server.Description);
            Assert.Equal("1.0.0", server.Version);
            Assert.False(result.Data.More);
        }

        [Fact]
        public async Task SaysWhichHeadersTheReaderMustFillIn()
        {
            var headers = """
                , "headers": [
                  { "name": "X-API-Key", "isRequired": true, "isSecret": true },
                  { "name": "X-Optional", "isRequired": false },
                  { "name": "Bad Header", "isRequired": true },
                  { "name": "x-api-key", "isRequired": true }
                ]
                """;
            _registry = _ => StubHttpMessageHandler.JsonResponse(Page(Server("io.example/keyed", Remote("https://k.example.com/mcp", headers: headers))));

            var server = Assert.Single((await Registry().SearchAsync("", default)).Data!.Servers);

            Assert.Equal(new[] { "X-API-Key" }, server.RequiredHeaders);
        }

        [Fact]
        public async Task WhatSomeonePublished_IsKeptPlainAndShort_AndOnlyHttpsLinks()
        {
            var extra = $$"""
                , "title": "Line one\nline‮two", "description": "{{new string('d', 400)}}",
                "websiteUrl": "javascript:alert(1)", "repository": { "url": "https://github.com/o/r", "source": "github" }
                """;
            _registry = _ => StubHttpMessageHandler.JsonResponse(Page(Server("io.example/x", Remote("https://x.example.com/mcp"), extra)));

            var server = Assert.Single((await Registry().SearchAsync("", default)).Data!.Servers);

            Assert.Equal("Line one linetwo", server.Title);
            Assert.Equal(McpRegistry.MaxDescriptionLength, server.Description.Length);
            Assert.EndsWith("…", server.Description);
            Assert.Null(server.WebsiteUrl);
            Assert.Equal("https://github.com/o/r", server.RepositoryUrl);
        }

        [Fact]
        public async Task AServerAlreadyInTheCatalog_NamesItsEntry_AndTheSameAddressIsListedOnce()
        {
            _registry = _ => StubHttpMessageHandler.JsonResponse(Page(
                Server("com.deepwiki/mcp", Remote("https://mcp.deepwiki.com/mcp")),
                Server("io.github.someone/deepwiki-copy", Remote("https://mcp.deepwiki.com/mcp/"))));

            var server = Assert.Single((await Registry().SearchAsync("", default)).Data!.Servers);

            Assert.Equal("deepwiki", server.CatalogKey);
        }

        [Fact]
        public async Task ShowsAtMostAPage_AndSaysThereIsMore()
        {
            var entries = Enumerable.Range(0, McpRegistry.MaxResults + 5)
                .Select(i => Server($"io.example/s{i}", Remote($"https://s{i}.example.com/mcp")))
                .ToArray();
            _registry = _ => StubHttpMessageHandler.JsonResponse(Page(entries));

            var result = (await Registry().SearchAsync("", default)).Data!;

            Assert.Equal(McpRegistry.MaxResults, result.Servers.Count);
            Assert.True(result.More);
        }

        [Fact]
        public void FieldsOfTheWrongShape_AreSkipped_NotThrown()
        {
            using var doc = JsonDocument.Parse("""
                { "servers": [ 1, { "server": "x" }, { "server": { "name": 5, "remotes": {} } },
                  { "server": { "name": "io.example/ok", "remotes": [ 3, { "type": "streamable-http", "url": "https://ok.example.com/mcp", "headers": "no" } ] } } ],
                  "metadata": "nope" }
                """);

            var page = McpRegistry.Parse(doc.RootElement);
            var server = Assert.Single(page.Servers);
            Assert.Null(page.NextCursor);
            Assert.Equal("io.example/ok", server.Name);
            Assert.Equal("", server.Description);
            Assert.Empty(server.RequiredHeaders);
        }

        [Fact]
        public async Task TheRegistryIsReadOnce_ForEverySearchAndReader()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());

            await Registry(cache).SearchAsync("GitHub", default);
            await Registry(cache).SearchAsync("weather", default);
            await Registry(cache).SearchAsync("", default);

            Assert.Single(_asked);
        }

        [Fact]
        public async Task ARegistryThatCannotBeRead_IsAnUpstreamError_AndIsAskedAgainNextTime()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            _registry = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var failed = await Registry(cache).SearchAsync("x", default);
            _registry = _ => StubHttpMessageHandler.JsonResponse("not json");
            var notJson = await Registry(cache).SearchAsync("x", default);

            Assert.Equal(ErrorCodes.ExternalApiError, failed.Error);
            Assert.Equal(ErrorCodes.ExternalApiError, notJson.Error);
            Assert.Equal(2, _asked.Count);
        }
    }
}
