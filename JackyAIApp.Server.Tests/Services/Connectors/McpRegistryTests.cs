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

        private static string Page(params string[] entries) => $$"""{ "servers": [{{string.Join(",", entries)}}], "metadata": { "count": {{entries.Length}} } }""";

        private static string Server(string name, string remotes, string extra = "", string status = "active") => $$"""
            { "server": { "name": "{{name}}", "description": "Does things.", "version": "1.0.0", "remotes": {{remotes}} {{extra}} },
              "_meta": { "io.modelcontextprotocol.registry/official": { "status": "{{status}}", "isLatest": true } } }
            """;

        private static string Remote(string url, string type = "streamable-http", string headers = "") =>
            $$"""[{ "type": "{{type}}", "url": "{{url}}" {{headers}} }]""";

        [Fact]
        public async Task AsksTheLatestVersionOfEachServer_WithTheSearchEscaped()
        {
            await Registry().SearchAsync("  git hub&x  ", default);

            var url = Assert.Single(_asked).RequestUri!.AbsoluteUri;
            Assert.StartsWith(McpRegistry.BaseUrl + "?", url);
            Assert.Contains("version=latest", url);
            Assert.Contains($"limit={McpRegistry.PageSize}", url);
            Assert.Contains("search=git%20hub%26x", url);
        }

        [Fact]
        public async Task AnEmptySearch_ListsFromTheStart()
        {
            await Registry().SearchAsync("   ", default);

            Assert.DoesNotContain("search=", Assert.Single(_asked).RequestUri!.ToString());
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

            var result = await Registry().SearchAsync("example", default);

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

            var server = Assert.Single((await Registry().SearchAsync("deepwiki", default)).Data!.Servers);

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
        public void ANextCursor_MeansThereIsMore()
        {
            using var doc = JsonDocument.Parse("""{ "servers": [], "metadata": { "nextCursor": "abc" } }""");

            Assert.True(McpRegistry.Parse(doc.RootElement).More);
        }

        [Fact]
        public void FieldsOfTheWrongShape_AreSkipped_NotThrown()
        {
            using var doc = JsonDocument.Parse("""
                { "servers": [ 1, { "server": "x" }, { "server": { "name": 5, "remotes": {} } },
                  { "server": { "name": "io.example/ok", "remotes": [ 3, { "type": "streamable-http", "url": "https://ok.example.com/mcp", "headers": "no" } ] } } ],
                  "metadata": "nope" }
                """);

            var server = Assert.Single(McpRegistry.Parse(doc.RootElement).Servers);
            Assert.Equal("io.example/ok", server.Name);
            Assert.Equal("", server.Description);
            Assert.Empty(server.RequiredHeaders);
        }

        [Fact]
        public async Task OneSearchIsAskedOnce_ForEveryReader()
        {
            var cache = new MemoryCache(new MemoryCacheOptions());

            await Registry(cache).SearchAsync("GitHub", default);
            await Registry(cache).SearchAsync("github", default);

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
