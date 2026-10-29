using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// Says in each <c>initialize</c> to a reader's MCP server that the chat draws MCP Apps (the
    /// <c>io.modelcontextprotocol/ui</c> extension, <c>capabilities.extensions</c>, SEP-1724), which the SDK's
    /// <c>ClientCapabilities</c> (1.3.0) cannot say: it has no <c>extensions</c>. The extension's own server SDK
    /// (<c>getUiCapability</c>) has a server offer its app tools only to a client that said so (docs/ai-native/design/09, D-203).
    /// Any other message, or a body that is not a JSON-RPC <c>initialize</c> with a capabilities object, goes through untouched;
    /// an <c>extensions</c> entry already there is kept.
    /// </summary>
    public sealed class McpAppsCapability(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        /// <summary>The extension's identifier.</summary>
        public const string ExtensionId = "io.modelcontextprotocol/ui";

        /// <summary>The most of a body read to look for <c>initialize</c>; one longer is not an initialize the SDK wrote.</summary>
        public const int MaxInitializeBytes = 64 * 1024;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // The SDK's body is JsonContent, whose length is not known before it is written (ContentLength null);
            // reading it buffers it, so it is sent the same when it is left as it is.
            if (request.Method == HttpMethod.Post && request.Content is { } content &&
                content.Headers.ContentLength is null or <= MaxInitializeBytes)
            {
                var body = await content.ReadAsByteArrayAsync(ct);
                var declared = body.Length <= MaxInitializeBytes ? Declare(body) : null;
                if (declared != null)
                {
                    var replaced = new ByteArrayContent(declared);
                    replaced.Headers.ContentType = content.Headers.ContentType ?? new MediaTypeHeaderValue("application/json");
                    request.Content = replaced;
                }
            }
            return await base.SendAsync(request, ct);
        }

        /// <summary>
        /// The body with the extension added to its capabilities, or null when it is not a JSON-RPC <c>initialize</c>
        /// with a capabilities object (left as it is) or already names the extension.
        /// </summary>
        internal static byte[]? Declare(byte[] body)
        {
            JsonNode? message;
            try
            {
                message = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
            if (message is not JsonObject request ||
                request["method"] is not JsonValue method || !method.TryGetValue<string>(out var name) || name != "initialize" ||
                request["params"] is not JsonObject parameters)
                return null;

            if (parameters["capabilities"] is null) parameters["capabilities"] = new JsonObject();
            if (parameters["capabilities"] is not JsonObject capabilities) return null;
            if (capabilities["extensions"] is null) capabilities["extensions"] = new JsonObject();
            if (capabilities["extensions"] is not JsonObject extensions || extensions.ContainsKey(ExtensionId)) return null;

            extensions[ExtensionId] = new JsonObject { ["mimeTypes"] = new JsonArray(McpToolClient.AppMimeType) };
            return JsonSerializer.SerializeToUtf8Bytes(request);
        }
    }
}
