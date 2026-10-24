using System.Text.Json;

namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>One tool a connector's server last listed, as kept with its installation.</summary>
    /// <param name="Name">The tool's own name on its server.</param>
    /// <param name="Title">What the reader is shown, if the server gave one.</param>
    /// <param name="Effect">What calling it does, as worked out when it was listed (an MCP server's
    /// <c>readOnlyHint</c> / <c>destructiveHint</c>).</param>
    /// <param name="InputSchema">Its JSON Schema, as the server sent it.</param>
    public sealed record CachedTool(string Name, string? Title, string Description, ToolEffect Effect, JsonElement InputSchema);

    /// <summary>
    /// The tools of an installation as stored (<c>ConnectorInstallations.ToolsCacheJson</c>, a JSON array).
    /// It was written from what an outside server said, so it is read leniently: an entry without a
    /// name is left out, one without an effect counts as a write, and anything unreadable is no tools.
    /// </summary>
    public static class CachedTools
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        public static IReadOnlyList<CachedTool> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
                var tools = new List<CachedTool>();
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var name = StringOf(e, "name");
                    if (string.IsNullOrEmpty(name)) continue;
                    var effect = Enum.TryParse<ToolEffect>(StringOf(e, "effect"), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                        ? parsed
                        : ToolEffect.Write;
                    var schema = e.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object
                        ? s.Clone()
                        : JsonSerializer.SerializeToElement(new { type = "object" });
                    tools.Add(new CachedTool(name, StringOf(e, "title"), StringOf(e, "description") ?? "", effect, schema));
                }
                return tools;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static string Serialize(IEnumerable<CachedTool> tools) => JsonSerializer.Serialize(tools, Json);

        private static string? StringOf(JsonElement e, string property) =>
            e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
